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
    public class GameClientBehavior : MonoBehaviour
    {
        private GameClient _client;
        private State _currentState;
        private NLogger _log;

        private UdpNetworkBehavior _network;

        public GameObject EntityPrefab;
        public GameObject PlayerPrefab;

        // Start is called before the first frame update
        private void Start()
        {
            _log = NLogManager.Instance.GetLogger(this);

            KinematicCharacterSystem.AutoSimulation = false;
            KinematicCharacterSystem.Interpolate = false;
            KinematicCharacterSystem.EnsureCreation();

            Observable.EveryFixedUpdate().Subscribe(_ =>
            {
                // Update physics
                KinematicCharacterSystem.Simulate(
                    Time.fixedDeltaTime,
                    KinematicCharacterSystem.CharacterMotors,
                    KinematicCharacterSystem.CharacterMotors.Count,
                    KinematicCharacterSystem.PhysicsMovers,
                    KinematicCharacterSystem.PhysicsMovers.Count);
            });

            _currentState = State.Connecting;

            _network = new UdpNetworkBehavior
            {
                ShouldBind = false,
                ShouldConnect = true
            };

            IConnectableObservable<long> connGeneratePacketEvents =
                Observable.EveryLateUpdate().Sample(TimeSpan.FromSeconds(Time.fixedDeltaTime)).Publish();

            IObservable<long> generatePacketEvents = connGeneratePacketEvents.RefCount();

            NetManagerRx netRx = new NetManagerRx(_network.RNetManager, _network.UdpMessageStream);

            IObservable<NetEvent> receivedDataStream =
                netRx.ReceivedNetEventStream
                    .Where(evt => evt.Type == NetEvent.EType.Receive)
                    .Do(_ => _log.Debug($"Received data: {_.DataReader.RawDataSize}"));

            netRx.ReceivedNetEventStream
                .Where(evt => evt.Type == NetEvent.EType.Connect)
                .Do(_ => _log.Info("Connected to server"))
                .Subscribe(evt =>
                {
                    _currentState = State.Playing;
                    _client = new GameClient(evt.Peer, false);

                    GameObject playerObj = Instantiate(PlayerPrefab);

                    KccControlledObject pco = new KccControlledObject
                    {
                        Entity = playerObj,
                        PlayerController = playerObj.GetComponent<CharacterController>(),
                        Controller = playerObj.GetComponent<MyCharacterController>()
                    };

                    KccControlledObjectSystemClient kccClient = new KccControlledObjectSystemClient();

                    pco.Controller.Motor.SetPosition(new Vector3(0, 2, 0));

                    //_client.ControlledObjectSys.CurrentlyControlledObject = pco;
                    kccClient.CurrentlyControlledObject = pco;

                    Observable.EveryUpdate().Sample(TimeSpan.FromMilliseconds(Time.fixedDeltaTime)).Subscribe(_ =>
                    {
                        kccClient.UpdateControlledObject();
                    });

                    PacketStreamRx psRx = new PacketStreamRx(
                        receivedDataStream.Where(e => e.Peer.Id == evt.Peer.Id),
                        Observable.EveryUpdate(),
                        generatePacketEvents);

                    // The order of these subscription sort of matters, or at least has implications that are sort of hidden 
                    // by this packet stream reactor.. so i might need to pull that out 
                    psRx.GamePacketStream
                        .Do(_ => _log.Debug("Received game packet stream event"))
                        .Select(kccClient.GetControlledObjectEventFromStream)
                        .BatchFrame(0, FrameCountType.FixedUpdate)
                        .Subscribe(serverUpdates =>
                        {
                            Debug.Log($"************ RUNNING {serverUpdates.Count} SERVER UPDATES *****************");
                            for (int i = 0; i < serverUpdates.Count; i++) kccClient.FixedUpdate_ServerReconcile(serverUpdates[i]);
                        });

                    psRx.OutgoingPacketStream
                        .Do(_ => _log.Debug("Writing to packet stream"))
                        .Subscribe(stream =>
                    {
                        kccClient.WriteToPacketStream(stream);
                        _client.Peer.Send(stream.Data,0, stream.Length, DeliveryMethod.Unreliable);
                    });

                    psRx.TransmissionNotificationStream
                        .Do(not => _log.Debug($"Next notification: {not}"))
                        .Subscribe(notification =>
                        {
                            //client.Replication.ReceiveNotification(notification);
                        });

                    psRx.Start();
                });


            //GameClientRx reactor =
            //    new GameClientRx(_network.RNetManager, _network.NetEventStream, EntityPrefab, PlayerPrefab);

            _network.Start();
            netRx.Start();

            connGeneratePacketEvents.Connect();
        }

        private enum State
        {
            Connecting,
            Ready,
            Playing
        }
    }
}