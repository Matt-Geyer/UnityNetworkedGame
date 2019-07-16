using System;
using System.Collections.Generic;
using System.Runtime.InteropServices.WindowsRuntime;
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

namespace Assets.Scripts
{
    // ReSharper disable once UnusedMember.Global
    public class GameClientBehavior : MonoBehaviour
    {
        private NLogger _log;
        private NetManager _netManager;
        private TimeSpan _timeoutCheckFrequency;
        private UdpServer _udpRx;

        [SerializeField] private CinemachineVirtualCamera _camera;
         
        // temporary
#pragma warning disable 649
        [SerializeField] private FloatControllerState.Serializable _walkBlendTree;
#pragma warning restore 649

        /// <summary>
        ///     The host to connect to
        /// </summary>
        public string ConnectAddress = "127.0.0.1";

        /// <summary>
        ///     The port to connect to
        /// </summary>
        public int ConnectPort = 40069;

        public int BindPort = 50069;

        //public GameObject EntityPrefab;
        public GameObject PlayerPrefab;

        // Start is called before the first frame update
        // ReSharper disable once UnusedMember.Local
        private void Start()
        {
            _log = NLogManager.Instance.GetLogger(this);
            _timeoutCheckFrequency = TimeSpan.FromSeconds(5);
            KinematicCharacterSystem.AutoSimulation = false;
            KinematicCharacterSystem.Interpolate = false;
            KinematicCharacterSystem.EnsureCreation();

            IConnectableObservable<FixedUpdateLoopEvents> connFixedUpdateEventStream = Observable
                .Create<FixedUpdateLoopEvents>(observer =>
                {
                    return Observable.EveryFixedUpdate().Subscribe(_ =>
                    {
                        // This defines the order of the events in each fixed update loop
                        observer.OnNext(FixedUpdateLoopEvents.Input);
                        observer.OnNext(FixedUpdateLoopEvents.Reconcile);
                        observer.OnNext(FixedUpdateLoopEvents.Physics);
                    });
                }).Publish();

            IObservable<FixedUpdateLoopEvents> inputFixedUpdateEvents =
                connFixedUpdateEventStream.Where(s => s == FixedUpdateLoopEvents.Input);
            IObservable<FixedUpdateLoopEvents> reconcileFixedUpdateEvents =
                connFixedUpdateEventStream.Where(s => s == FixedUpdateLoopEvents.Reconcile);
            IObservable<FixedUpdateLoopEvents> physicsFixedUpdateEvents =
                connFixedUpdateEventStream.Where(s => s == FixedUpdateLoopEvents.Physics);

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

            Observable.EveryUpdate().Subscribe(_ => KinematicCharacterSystem.CustomInterpolationUpdate());


            Player rewiredPlayer = ReInput.players.GetPlayer(0);

           
            UserInputSample sample = new UserInputSample();

            SlidingList<UserInputSample> inputList =
                new SlidingList<UserInputSample>(500, () => new UserInputSample {MoveDirection = new Vector3()});


            var connPlayerInputStream = Observable.Create<UserInputSample>(observer =>
            {
                UserInputSample next = new UserInputSample {MoveDirection = new Vector3()};

                return Observable.EveryUpdate().Subscribe(_ =>
                {
                    next.MoveDirection.z = rewiredPlayer.GetAxis("MoveVertical");
                    next.MoveDirection.x = rewiredPlayer.GetAxis("MoveHorizontal");
                    observer.OnNext(next);
                });
            }).Publish();

            var playerInputStream = connPlayerInputStream.RefCount();
            
            _udpRx = new UdpServer
            {
                BindPort = BindPort
            };

            IConnectableObservable<long> connGeneratePacketEvents =
                Observable.EveryEndOfFrame().Sample(TimeSpan.FromSeconds(Time.fixedDeltaTime)).Publish();

            IObservable<long> generatePacketEvents = connGeneratePacketEvents.RefCount();

            _netManager = new NetManager(null) {DisconnectTimeout = 5};

            IDisposable connectSub = Observable.EveryUpdate()
                .Sample(TimeSpan.FromSeconds(1))
                .Do(_ => Debug.Log("Trying to connect"))
                .Subscribe(_ => _netManager.Connect(ConnectAddress, ConnectPort, "somekey"));

            NetManagerRx netRx = new NetManagerRx(_netManager, _udpRx.UdpMessageStream);

            Observable.EveryLateUpdate().Sample(_timeoutCheckFrequency).Subscribe(_ => { _netManager.Update(); });

            _netManager.UdpSendEvents
                .Subscribe(e =>
                {
                    _udpRx.OutgoingUdpMessageSender.Send(e.Message, e.Start, e.Length, e.RemoteEndPoint,
                        UdpSendType.SendTo);
                });

            IObservable<NetEvent> receivedDataStream =
                netRx.ReceivedNetEventStream
                    .Where(evt => evt.Type == NetEvent.EType.Receive);

            netRx.ReceivedNetEventStream
                .Where(evt => evt.Type == NetEvent.EType.Connect)
                .Do(_ => _log.Info($"Connected to server with ID: {_.Peer.Id}"))
                .Subscribe(evt =>
                {
                    connectSub.Dispose();
                    
                    GameObject playerObj = Instantiate(PlayerPrefab);

                    _camera.Follow = playerObj.transform;

                    

                    _camera.LookAt = playerObj.transform;
                    

                    var animancerComponent = playerObj.GetComponentInChildren<AnimancerComponent>();

                    KccControlledObject pco = new KccControlledObject
                    {
                        Entity = playerObj,
                        PlayerController = playerObj.GetComponent<CharacterController>(),
                        Controller = playerObj.GetComponent<MyCharacterController>()
                    };

                    KccControlledObjectSystemClient kccClient = new KccControlledObjectSystemClient();

                    pco.Controller.Motor.SetPosition(new Vector3(0, 2, 0));

                    kccClient.CurrentlyControlledObject = pco;

                    SlidingList<MoveInfo> networkMoves = new SlidingList<MoveInfo>(500, () => new MoveInfo());

                    int seqLastProcessedByServer;

                    var networkPlayerInputStream =
                        Observable.Create<MoveInfo>(observer =>
                        {
                            playerInputStream
                                .BatchFrame(0, FrameCountType.FixedUpdate)
                                .Subscribe(inputSamples =>
                                {
                                    if (inputSamples.Count == 0) return;

                                    MoveInfo move = networkMoves.GetNextAvailable();

                                    if (move == null) return;

                                    move.UserInput.MoveDirection = inputSamples[inputSamples.Count - 1].MoveDirection;
                                    move.UserInput.Seq = move.Seq; // todo
                                    move.MotorState = pco.Controller.Motor.GetState();

                                    observer.OnNext(move);

                                });
                            return Disposable.Empty;
                        }).Publish();


                    List<UserInputSample> lastThreeMoves = new List<UserInputSample>(3);

                    for (int m = 0; m < 3; m++)
                    {
                        var move = networkMoves.GetNextAvailable();
                        move.UserInput.Seq = move.Seq;
                        lastThreeMoves.Add(move.UserInput);
                    }

                    networkPlayerInputStream
                        .Do(_ => _log.Debug($"Setting user input on player controller: {_.UserInput.MoveDirection}"))
                        .Subscribe(_ =>
                    {
                        pco.Controller.SetInputs(ref _.UserInput);
                    });

                    networkPlayerInputStream
                        .Do(_ => _log.Debug($"Updating lastThreeMoves: {_.UserInput.MoveDirection}"))
                        .Subscribe(mi =>
                    {
                        lastThreeMoves.RemoveAt(0);
                        lastThreeMoves.Add(mi.UserInput);
                    });


                    List<KinematicCharacterMotor> playerMotor = new List<KinematicCharacterMotor> { pco.Controller.Motor };

                    Queue<ControlledObjectServerEvent> updateQueue = new Queue<ControlledObjectServerEvent>();
                    reconcileFixedUpdateEvents.Subscribe(_ =>
                    {
                        for (int i = updateQueue.Count; i > 0; i--)
                        {
                            ControlledObjectServerEvent update = updateQueue.Dequeue();

                            seqLastProcessedByServer = update.SeqLastProcessed;

                            var stateAtSequence = networkMoves.AckSequence((ushort)seqLastProcessedByServer);

                            if (networkMoves.Items.Count <= 0 || stateAtSequence == null) return;

                            Vector3 difference = stateAtSequence.MotorState.Position - update.MotorState.Position;

                            var cs = pco.Controller.Motor.GetState();
                            float distance = difference.magnitude;

                            _log.Debug($"Sequence: {stateAtSequence.Seq} SeqLastProcessed: {seqLastProcessedByServer}");
                            _log.Debug($"Server Position: ({update.MotorState.Position.x},{update.MotorState.Position.y},{update.MotorState.Position.z})");
                            _log.Debug($"Client Position: ({stateAtSequence.MotorState.Position.x},{stateAtSequence.MotorState.Position.y},{stateAtSequence.MotorState.Position.z})");
                            _log.Debug($"Distance: {distance}");

                            if (distance > 2)
                            {
                                // correct
                                cs.Position = update.MotorState.Position;
                                cs.AttachedRigidbodyVelocity = update.MotorState.AttachedRigidbodyVelocity;
                                cs.BaseVelocity = update.MotorState.BaseVelocity;

                                pco.Controller.Motor.ApplyState(cs);

                                // clear input window?
                                networkMoves.Items.Clear();
                            }
                            else if (distance > .0001)
                            {
                                stateAtSequence.MotorState.Position = update.MotorState.Position;
                                stateAtSequence.MotorState.AttachedRigidbodyVelocity =
                                    update.MotorState.AttachedRigidbodyVelocity;
                                stateAtSequence.MotorState.BaseVelocity = update.MotorState.BaseVelocity;

                                pco.Controller.Motor.ApplyState(stateAtSequence.MotorState);

                                for (int s = 0; s < networkMoves.Items.Count; s++)
                                {
                                    UserInputSample input = networkMoves.Items[s].UserInput;

                                    pco.Controller.SetInputs(ref input);

                                    KinematicCharacterSystem.Simulate(Time.fixedDeltaTime, playerMotor, 1, null, 0);
                                }

                                // what is distance between what we actually are and what we now calculate we should be at
                                KinematicCharacterMotorState predictedState = pco.Controller.Motor.GetState();
                                Vector3 difVector3 = predictedState.Position - cs.Position;

                                DebugGraph.Log("Prediction Mismatch", difVector3.magnitude);

                                if (difVector3.magnitude >= .0001)
                                {
                                    cs.Position += difVector3 * 0.1f;
                                }
                                else
                                {
                                    cs.Position = predictedState.Position;
                                }

                                //kcc.Controller.Motor.ApplyState(cs);
                                pco.Controller.Motor.SetPosition(cs.Position);
                            }
                        }
                    });


                    // ANIMATION
                   animancerComponent.Transition(_walkBlendTree);
                   _walkBlendTree.State.Playable.SetBool("Walking", true);
                    //Observable.EveryLateUpdate()
                    playerInputStream    
                        .Subscribe(_ =>
                    {
                        // Get values from kcc and set them on animancer
                        //_walkBlendTree.State.Playable.SetFloat("RelativeVertical", pco.Controller.Motor.BaseVelocity.z);
                        //_walkBlendTree.State.Playable.SetFloat("RelativeHorizontal", pco.Controller.Motor.BaseVelocity.x);
                        //_walkBlendTree.State.Playable.SetFloat("Speed", pco.Controller.Motor.BaseVelocity.magnitude);

                        _walkBlendTree.State.Playable.SetFloat("RelativeVertical", _.MoveDirection.x);
                        _walkBlendTree.State.Playable.SetFloat("RelativeHorizontal", _.MoveDirection.z);
                        _walkBlendTree.State.Playable.SetFloat("Speed", _.MoveDirection.magnitude);
                    });

                    PacketStreamRx psRx = new PacketStreamRx(
                        receivedDataStream.Where(e => e.Peer.Id == evt.Peer.Id),
                        Observable.EveryUpdate(),
                        generatePacketEvents);

                    // The order of these subscription sort of matters, or at least has implications that are sort of hidden 
                    // by this packet stream reactor.. so i might need to pull that out 
                    psRx.GamePacketStream
                        .Select(kccClient.GetControlledObjectEventFromStream)
                        .BatchFrame(0, FrameCountType.FixedUpdate)
                        .Subscribe(serverUpdates =>
                        {
                            for (int i = 0; i < serverUpdates.Count; i++) updateQueue.Enqueue(serverUpdates[i]);
                        });

                    psRx.OutgoingPacketStream
                        .Subscribe(stream =>
                        {
                            _log.Debug($"Before Wrote player move sequences: { stream.Length }");
                            // Write last three client inputs
                            lastThreeMoves[0].Serialize(stream);
                            lastThreeMoves[1].Serialize(stream);
                            lastThreeMoves[2].Serialize(stream);

                            _log.Debug($"After Wrote player move sequences: { stream.Length }");

                            evt.Peer.Send(stream.Data, 0, stream.Length, DeliveryMethod.Unreliable);
                        });

                    psRx.TransmissionNotificationStream
                        .Subscribe(notification =>
                        {
                            //client.Replication.ReceiveNotification(notification);
                        });


                    networkPlayerInputStream.Connect();

                    psRx.Start();
                });

            _netManager.StartUdpSendEvents();
            _udpRx.Start();
            netRx.Start();
            connFixedUpdateEventStream.Connect();
            connGeneratePacketEvents.Connect();
        }

        // ReSharper disable once UnusedMember.Local
        private void OnDestroy()
        {
            _udpRx.Stop();
        }

        private enum FixedUpdateLoopEvents
        {
            Physics,
            Input,
            Reconcile
        }
    }
}