using System;
using AiUnity.NLog.Core;
using Assets.Scripts.CharacterControllerStuff;
using Assets.Scripts.Network;
using Assets.Scripts.Network.StreamSystems;
using KinematicCharacterController;
using LiteNetLib;
using UniRx;
using UnityEngine;

namespace Assets.Scripts
{
    public class GameServerRx
    {
        private readonly UdpServer _udpServer;
        private readonly NLogger _log;

        public GameServerRx(GameServerRxOptions options)
        {
            _log = NLogManager.Instance.GetLogger(this);
            
            _log.Info("GameServerRx initializing..");
           TimeSpan timeoutCheckFrequency = TimeSpan.FromSeconds(options.CheckTimeoutFrequencySeconds);

            _udpServer = new UdpServer
            {
                BindPort = options.BindPort,
                BindAddress = options.BindAddress
            };

            KinematicCharacterSystem.AutoSimulation = false;
            KinematicCharacterSystem.Interpolate = false;
            KinematicCharacterSystem.EnsureCreation();

            Observable.EveryFixedUpdate().Subscribe(_ =>
            {
                // update kinematic character physics
                KinematicCharacterSystem.Simulate(
                    Time.fixedDeltaTime,
                    KinematicCharacterSystem.CharacterMotors,
                    KinematicCharacterSystem.CharacterMotors.Count,
                    KinematicCharacterSystem.PhysicsMovers,
                    KinematicCharacterSystem.PhysicsMovers.Count);
            });

            IConnectableObservable<long> connGeneratePacketEvents =
                Observable.EveryEndOfFrame().Sample(TimeSpan.FromSeconds(Time.fixedDeltaTime)).Publish();

            IObservable<long> generatePacketEvents = connGeneratePacketEvents.RefCount();

            NetManager netManager = new NetManager(null) { DisconnectTimeout = 30 };

            NetManagerRx netRx = new NetManagerRx(netManager, _udpServer.UdpMessageStream);

            Observable.EveryLateUpdate().Sample(timeoutCheckFrequency).Subscribe(_ => { netManager.Update(); });

            netManager.UdpSendEvents
                .Subscribe(e =>
                {
                    _udpServer.OutgoingUdpMessageSender.Send(e.Message, e.Start, e.Length, e.RemoteEndPoint, UdpSendType.SendTo);
                });

            IObservable<NetEvent> receivedDataStream = netRx.ReceivedNetEventStream
                .Where(evt => evt.Type == NetEvent.EType.Receive);

            netRx.ReceivedNetEventStream
                .Where(evt => evt.Type == NetEvent.EType.ConnectionRequest)
                .Do(_ => _log.Info("Got connection request."))
                .Subscribe(evt => evt.ConnectionRequest.Accept());

            netRx.ReceivedNetEventStream
                .Where(evt => evt.Type == NetEvent.EType.Connect)
                .Do(evt => _log.Info($"Client Connected with id: {evt.Peer.Id} "))
                .Subscribe(evt =>
                {
                    GameObject clientGameObj = UnityEngine.Object.Instantiate(options.PlayerPrefab);

                    KccControlledObject kcc = new KccControlledObject
                    {
                        Entity = clientGameObj,
                        PlayerController = clientGameObj.GetComponent<CharacterController>(),
                        Controller = clientGameObj.GetComponent<MyCharacterController>()
                    };

                    kcc.Controller.Motor.SetPosition(new Vector3(0, 2, 0));

                    KccControlledObjectSystemServer kccServer = new KccControlledObjectSystemServer
                    {
                        CurrentlyControlledObject = kcc
                    };

                    PacketStreamRx psRx = new PacketStreamRx(
                        receivedDataStream.Where(e => e.Peer.Id == evt.Peer.Id),
                        Observable.EveryUpdate(),
                        generatePacketEvents);

                    EventSystemRx eventRx = new EventSystemRx();
                    int tempI = 0;
                    Observable.EveryFixedUpdate().SampleFrame(10).Subscribe(_ =>
                    {
                        if (eventRx.QueueEvent(new TestEvent {Message = $"WHAT UP {tempI}", IsReliable = true}))
                        {
                            tempI++;
                        }
                        else
                        {
                            Debug.Log("Remote window is full!!");
                        }
                    });

                    eventRx.EventStream
                        .Where(ue => ue.GetType() == typeof(TestEvent))
                        .Select(ue => ue as TestEvent)
                        .Subscribe(te => {  });

                    int seqLastProcessed = -1;

                    IDisposable controlledObjectEvents = psRx.GamePacketStream
                        .Select(kccServer.GetClientEventFromStream)
                        .Do(_ => _log.Debug($"Got controlledObj event: {_.PlayerInputs[2].Seq}"))
                        .Subscribe(controlledObjEvent =>
                        {
                            // In a 0 packet loss scenario Items [1] was last sequence and input [2] is this sequence
                            // but we will look further back, and if they are all new then apply all 3 moves        
                            ushort nextMoveSeq = (ushort)(seqLastProcessed + 1);
                            _log.Debug($"LastProcessedMoveSeq: {seqLastProcessed} NextMove: {nextMoveSeq}");
                            int i = 2;
                            for (; i >= 0; i--)
                            {
                                _log.Debug($"_playerInputsToTransmit[{i}].seq: {controlledObjEvent.PlayerInputs[i].Seq}");
                                if (controlledObjEvent.PlayerInputs[i].Seq == nextMoveSeq) break;
                            }

                            // if nextMoveSeq isn't found then i will be -1
                            if (i == -1)
                            {
                                if (!SequenceHelper.SeqIsAheadButInsideWindow(nextMoveSeq, controlledObjEvent.PlayerInputs[0].Seq, 360))
                                {
                                    _log.Debug($"No player moves since sequence: {seqLastProcessed}");
                                    // CurrentlyControlledObject.ApplyMoveDirection(0,0);
                                    return;
                                }

                                i = 0;
                            }

                            // This should always have at least one new move but up to 3
                            for (int j = i; j <= 2; j++)
                            {
                                _log.Debug($"Looking at _playerInputsToTransmit[{j}] - {controlledObjEvent.PlayerInputs[j].MoveDirection}");
                                kcc.Controller.SetInputs(ref controlledObjEvent.PlayerInputs[j]);
                                // simulate?
                                seqLastProcessed = controlledObjEvent.PlayerInputs[j].Seq;
                            }
                        });

                    IDisposable readUngEvents = psRx.GamePacketStream.Subscribe(eventRx.ReadStream);


                    IDisposable outgoingPacketSub = psRx.OutgoingPacketStream
                        .Subscribe(stream =>
                        {
                            // controlled object sys
                            stream.Put((ushort)seqLastProcessed);
                            kcc.Serialize(stream);

                            // rep sys

                            // event sys
                            eventRx.WriteToStream(stream);

                            evt.Peer.Send(stream.Data, 0, stream.Length, DeliveryMethod.Unreliable);
                        });

                    IDisposable transmissionNotificationSub = psRx.TransmissionNotificationStream
                        .Subscribe(received =>
                        {
                            eventRx.HandleTransmissionNotification(received);
                        });

                    // Take(1) ensures this will dispose after the first disconnect event it receives
                    netRx.ReceivedNetEventStream
                        .Where(e => e.Type == NetEvent.EType.Disconnect && e.Peer.Id == evt.Peer.Id)
                        .Take(1)
                        .Do(_ => _log.Info($"CLIENT: {_.Peer.Id} disconnected!"))
                        .Subscribe(
                            _ =>
                            {
                                UnityEngine.Object.Destroy(clientGameObj);
                                transmissionNotificationSub.Dispose();
                                outgoingPacketSub.Dispose();
                                controlledObjectEvents.Dispose();
                                readUngEvents.Dispose();
                            });

                    psRx.Start();
                    eventRx.Start();
                });

            // Start network thread
            netManager.StartUdpSendEvents();
            _udpServer.Start();
            netRx.Start();
            connGeneratePacketEvents.Connect();
        }

        public void Stop()
        {
            _udpServer.Stop();
            _log.Info("UdpServer stopped");

        }
    }
}