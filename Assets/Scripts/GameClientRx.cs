using System;
using System.Collections.Generic;
using AiUnity.NLog.Core;
using Animancer;
using Assets.Scripts.CharacterControllerStuff;
using Assets.Scripts.Network;
using Assets.Scripts.Network.StreamSystems;
using Cinemachine;
using KinematicCharacterController;
using LiteNetLib;
using Rewired;
using UniRx;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Assets.Scripts
{
    public sealed class GameClientRx
    {
        private readonly NLogger _log;
        private UdpServer _udpRx;


        public GameClientRx(GameClientRxOptions gameClientRxOptions)
        {
            CinemachineVirtualCamera virtualCamera = gameClientRxOptions.Camera;
            FloatControllerState.Serializable walkBlendTree1 = gameClientRxOptions.WalkBlendTree;
            _log = NLogManager.Instance.GetLogger(this);
            TimeSpan timeoutCheckFrequency = TimeSpan.FromSeconds(gameClientRxOptions.CheckTimeoutFrequencySeconds);
            GameObject playerPrefab = gameClientRxOptions.PlayerPrefab;

            // Every fixed update emit an event for server reconciliation and then physics update
            IConnectableObservable<FixedUpdateLoopEvents> connFixedUpdateEventStream = 
                Observable.Create<FixedUpdateLoopEvents>(observer =>
                {
                    return Observable.EveryFixedUpdate().Subscribe(_ =>
                    {
                        // This defines the order of the events in each fixed update loop
                        observer.OnNext(FixedUpdateLoopEvents.Reconcile);
                        observer.OnNext(FixedUpdateLoopEvents.Physics);
                    });
                }).Publish();
            
            // Create observables for the inner fixed update events
            IObservable<FixedUpdateLoopEvents> reconcileFixedUpdateEvents =
                connFixedUpdateEventStream.Where(s => s == FixedUpdateLoopEvents.Reconcile);
            IObservable<FixedUpdateLoopEvents> physicsFixedUpdateEvents =
                connFixedUpdateEventStream.Where(s => s == FixedUpdateLoopEvents.Physics);

            // Turn off automatic simulation and interpolation in the KCS
            KinematicCharacterSystem.AutoSimulation = false;
            KinematicCharacterSystem.Interpolate = false;
            KinematicCharacterSystem.EnsureCreation();

            // Update the KCS system every fixed update
            physicsFixedUpdateEvents
                .Subscribe(_ =>
                {
                    KinematicCharacterSystem.PreSimulationInterpolationUpdate(Time.fixedDeltaTime);

                    // Update physics
                    KinematicCharacterSystem.Simulate(
                        Time.fixedDeltaTime,
                        KinematicCharacterSystem.CharacterMotors,
                        KinematicCharacterSystem.CharacterMotors.Count,
                        KinematicCharacterSystem.PhysicsMovers,
                        KinematicCharacterSystem.PhysicsMovers.Count);

                    KinematicCharacterSystem.PostSimulationInterpolationUpdate(Time.fixedDeltaTime);
                });

            // Do per frame interpolation
            Observable.EveryUpdate().Subscribe(_ => KinematicCharacterSystem.CustomInterpolationUpdate());
            

            // PLAYER INPUT STREAM
            Player rewiredPlayer = ReInput.players.GetPlayer(0);
            
            // Every update sample the players input and emit
            IConnectableObservable<UserInputSample> connPlayerInputStream = Observable.Create<UserInputSample>(
                observer =>
                {
                    UserInputSample next = new UserInputSample {MoveDirection = new Vector3()};

                    return Observable.EveryUpdate().Subscribe(_ =>
                    {
                        next.MoveDirection.z = rewiredPlayer.GetAxis("MoveVertical");
                        next.MoveDirection.x = rewiredPlayer.GetAxis("MoveHorizontal");
                        observer.OnNext(next);
                    });
                }).Publish();
            IObservable<UserInputSample> playerInputStream = connPlayerInputStream.RefCount();


            // NETWORK
            
            _udpRx = new UdpServer { BindPort = gameClientRxOptions.BindPort };
            NetManager netManager = new NetManager(null) { DisconnectTimeout = 30 };
            NetManagerRx netRx = new NetManagerRx(netManager, _udpRx.UdpMessageStream);

            // Emit an event during late update after the specified period of time that signals
            // when to send a new packet
            TimeSpan sendPacketFrequency = TimeSpan.FromSeconds(Time.fixedDeltaTime);
            IConnectableObservable<long> connGeneratePacketEvents =
                Observable.EveryEndOfFrame().
                    Sample(sendPacketFrequency)
                    .Publish();
            IObservable<long> generatePacketEvents = connGeneratePacketEvents.RefCount();

            // Try to connect every second
            IDisposable connectSub = Observable.Timer(DateTimeOffset.Now, TimeSpan.FromSeconds(1))
                .Do(_ => Debug.Log("Trying to connect"))
                .Subscribe(_ => netManager.Connect(gameClientRxOptions.ConnectAddress, gameClientRxOptions.ConnectPort, "somekey"));

            // Run NetManager update logic 
            Observable.EveryLateUpdate().Sample(timeoutCheckFrequency).Subscribe(_ => netManager.Update());

            // Subscribe to NetManagers Send event stream and send them using the udp server
            netManager.UdpSendEvents
                .Subscribe(e =>
                {
                    _udpRx.OutgoingUdpMessageSender.Send(e.Message, e.Start, e.Length, e.RemoteEndPoint, UdpSendType.SendTo);
                });

            
            IObservable<NetEvent> receivedDataStream =
                netRx.ReceivedNetEventStream
                    .Where(evt => evt.Type == NetEvent.EType.Receive);

            netRx.ReceivedNetEventStream
                .Where(evt => evt.Type == NetEvent.EType.Connect)
                .Do(_ => _log.Info($"Connected to server with ID: {_.Peer.Id}"))
                .Subscribe(evt =>
                {
                    // Stop trying to connect now that we are connected
                    connectSub.Dispose();
                    
                    GameObject playerObj = Object.Instantiate(playerPrefab);

                    // Virtual Camera properties
                    virtualCamera.Follow = playerObj.transform;
                    virtualCamera.LookAt = playerObj.transform;

                    // Reference animancer to play animations
                    AnimancerComponent animancerComponent = playerObj.GetComponentInChildren<AnimancerComponent>();

                    // KinematicCharacterController implementation -- haven't changed this from demo at all except to take UserInputSamples
                    MyCharacterController charController = playerObj.GetComponent<MyCharacterController>();

                    // Set character starting position
                    charController.Motor.SetPosition(new Vector3(0, 2, 0));

                    // Keep a sliding window of sequenced moves that will be sent to and ACKed by the server
                    SlidingList<MoveInfo> networkMoves = new SlidingList<MoveInfo>(500, () => new MoveInfo());
                    
                    // Send the last three moves to the client to increase chances of them being processed
                    List<UserInputSample> lastThreeMoves = new List<UserInputSample>(3);
                    for (int m = 0; m < 3; m++)
                    {
                        MoveInfo move = networkMoves.GetNextAvailable();
                        move.UserInput.Seq = move.Seq;
                        lastThreeMoves.Add(move.UserInput);
                    }

              
                    // Batch player input samples which are produced every frame 
                    IConnectableObservable<MoveInfo> networkPlayerInputStream =
                        Observable.Create<MoveInfo>(observer =>
                        {
                            playerInputStream
                                .BatchFrame(0, FrameCountType.FixedUpdate)
                                .Subscribe(inputSamples =>
                                {
                                    if (inputSamples.Count == 0) return;

                                    MoveInfo move = networkMoves.GetNextAvailable();

                                    if (move == null) return;

                                    // Todo - look at all sampled moves and flatten into one
                                    move.UserInput.MoveDirection = inputSamples[inputSamples.Count - 1].MoveDirection;
                                    move.UserInput.Seq = move.Seq; // todo
                                    move.MotorState = charController.Motor.GetState();
                                    
                                    observer.OnNext(move);
                                });
                            return Disposable.Empty;
                        }).Publish();

                    networkPlayerInputStream
                        .Subscribe(mi =>
                        {
                            // Set inputs on character
                            charController.SetInputs(ref mi.UserInput);
                            // Update moves to transmit
                            lastThreeMoves.RemoveAt(0);
                            lastThreeMoves.Add(mi.UserInput);
                        });


                    // Client side reconciliation  
                    int seqLastProcessedByServer;
                    List<KinematicCharacterMotor> playerMotor = new List<KinematicCharacterMotor> {charController.Motor};
                    Queue<ControlledObjectServerEvent> updateQueue = new Queue<ControlledObjectServerEvent>();
                    reconcileFixedUpdateEvents.Subscribe(_ =>
                    {
                        for (int i = updateQueue.Count; i > 0; i--)
                        {
                            ControlledObjectServerEvent update = updateQueue.Dequeue();

                            seqLastProcessedByServer = update.SeqLastProcessed;

                            MoveInfo stateAtSequence = networkMoves.AckSequence((ushort) seqLastProcessedByServer);

                            if (networkMoves.Items.Count <= 0 || stateAtSequence == null) return;

                            Vector3 difference = stateAtSequence.MotorState.Position - update.MotorState.Position;

                            KinematicCharacterMotorState currentMotorState = charController.Motor.GetState();
                            float distance = difference.magnitude;

                            _log.Debug($"Sequence: {stateAtSequence.Seq} SeqLastProcessed: {seqLastProcessedByServer}");
                            _log.Debug(
                                $"Server Position: ({update.MotorState.Position.x},{update.MotorState.Position.y},{update.MotorState.Position.z})");
                            _log.Debug(
                                $"Client Position: ({stateAtSequence.MotorState.Position.x},{stateAtSequence.MotorState.Position.y},{stateAtSequence.MotorState.Position.z})");
                            _log.Debug($"Distance: {distance}");

                            if (distance > 2)
                            {
                                // correct
                                currentMotorState.Position = update.MotorState.Position;
                                currentMotorState.AttachedRigidbodyVelocity = update.MotorState.AttachedRigidbodyVelocity;
                                currentMotorState.BaseVelocity = update.MotorState.BaseVelocity;

                                charController.Motor.ApplyState(currentMotorState);

                                // clear input window?
                                networkMoves.Items.Clear();
                            }
                            else if (distance > .0001)
                            {
                                stateAtSequence.MotorState.Position = update.MotorState.Position;
                                stateAtSequence.MotorState.AttachedRigidbodyVelocity =
                                    update.MotorState.AttachedRigidbodyVelocity;
                                stateAtSequence.MotorState.BaseVelocity = update.MotorState.BaseVelocity;

                                charController.Motor.ApplyState(stateAtSequence.MotorState);

                                for (int s = 0; s < networkMoves.Items.Count; s++)
                                {
                                    UserInputSample input = networkMoves.Items[s].UserInput;

                                    charController.SetInputs(ref input);

                                    KinematicCharacterSystem.Simulate(Time.fixedDeltaTime, playerMotor, 1, null, 0);
                                }

                                // what is distance between what we actually are and what we now calculate we should be at
                                KinematicCharacterMotorState predictedState = charController.Motor.GetState();
                                Vector3 difVector3 = predictedState.Position - currentMotorState.Position;

                                DebugGraph.Log("Prediction Mismatch", difVector3.magnitude);

                                if (difVector3.magnitude >= .0001)
                                    currentMotorState.Position += difVector3 * 0.1f;
                                else
                                    currentMotorState.Position = predictedState.Position;
                                charController.Motor.SetPosition(currentMotorState.Position);
                            }
                        }
                    });


                    // ANIMATION
                    animancerComponent.Transition(walkBlendTree1);
                    walkBlendTree1.State.Playable.SetBool("Walking", true);

                    playerInputStream
                        .Subscribe(inputSample =>
                        {
                            walkBlendTree1.State.Playable.SetFloat("RelativeVertical", inputSample.MoveDirection.x);
                            walkBlendTree1.State.Playable.SetFloat("RelativeHorizontal", inputSample.MoveDirection.z);
                            walkBlendTree1.State.Playable.SetFloat("Speed", inputSample.MoveDirection.magnitude);
                        });

                    PacketStreamRx psRx = new PacketStreamRx(
                        receivedDataStream.Where(e => e.Peer.Id == evt.Peer.Id),
                        Observable.EveryUpdate(),
                        generatePacketEvents);
                    
                    EventSystemRx eventRx = new EventSystemRx();

                    int replyId = 0;

                    eventRx.EventStream
                        .Where(ue => ue.GetType() == typeof(TestEvent))
                        .Select(ue => ue as TestEvent)
                        .Subscribe(ue =>
                        {
                            //eventRx.QueueEvent(new TestEvent {IsReliable = false, Message = $"GOT UR MESSAGE: {replyId++}"});
                        });

                    psRx.GamePacketStream
                        .Select(stream =>
                        {
                            ControlledObjectServerEvent frame = new ControlledObjectServerEvent
                            {
                                SeqLastProcessed = stream.GetUShort()
                            };
                            SerializationHelper.DeserializeMotorState(ref frame.MotorState, stream);
                            return frame;
                        })
                        .BatchFrame(0, FrameCountType.FixedUpdate)
                        .Subscribe(serverUpdates =>
                        {
                            for (int i = 0; i < serverUpdates.Count; i++) updateQueue.Enqueue(serverUpdates[i]);
                        });

                    psRx.GamePacketStream.Subscribe(eventRx.ReadStream);

                    psRx.OutgoingPacketStream
                        .Subscribe(stream =>
                        {
                            // Write last three client inputs
                            lastThreeMoves[0].Serialize(stream);
                            lastThreeMoves[1].Serialize(stream);
                            lastThreeMoves[2].Serialize(stream);
                           
                            eventRx.WriteToStream(stream);

                            evt.Peer.Send(stream.Data, 0, stream.Length, DeliveryMethod.Unreliable);
                        });

                    psRx.TransmissionNotificationStream
                        .Subscribe(notification =>
                        {
                            eventRx.HandleTransmissionNotification(notification);
                        });


                    networkPlayerInputStream.Connect();

                    psRx.Start();
                    eventRx.Start();
                });

            netManager.StartUdpSendEvents();
            _udpRx.Start();
            netRx.Start();
            connFixedUpdateEventStream.Connect();
            connGeneratePacketEvents.Connect();
            
        }

        public void Stop()
        {
            _udpRx.Stop();
        }

        private enum FixedUpdateLoopEvents
        {
            Physics,
            Reconcile
        }
    }
}