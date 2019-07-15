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
    // ReSharper disable once UnusedMember.Global
    public class GameServerBehavior : MonoBehaviour
    {
        private NLogger _log;
        private NetManager _netManager;
        private TimeSpan _timeoutCheckFrequency;
        private UdpServer _udpRx;

        /// <summary>
        ///     The local endpoint address to bind to
        /// </summary>
        public string BindAddress = "0.0.0.0";

        /// <summary>
        ///     The local endpoint port to bind the socket to
        /// </summary>
        public int BindPort = 40069;

        /// <summary>
        ///     How often the CheckTimeout logic should run
        /// </summary>
        public float CheckTimeoutFrequencySeconds = 1.0f;

        //public GameObject ObjectPrefab;

        public GameObject PlayerPrefab;

        // Start is called before the first frame update
        // ReSharper disable once UnusedMember.Local
        private void Start()
        {
            _log = NLogManager.Instance.GetLogger(this);
            _timeoutCheckFrequency = TimeSpan.FromSeconds(CheckTimeoutFrequencySeconds);

            _udpRx = new UdpServer
            {
                BindPort = BindPort,
                BindAddress = BindAddress
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
                Observable.EveryUpdate().Sample(TimeSpan.FromSeconds(Time.fixedDeltaTime)).Publish();

            IObservable<long> generatePacketEvents = connGeneratePacketEvents.RefCount();

            _netManager = new NetManager(null) {DisconnectTimeout = 5};

            NetManagerRx netRx = new NetManagerRx(_netManager, _udpRx.UdpMessageStream);

            Observable.EveryLateUpdate().Sample(_timeoutCheckFrequency).Subscribe(_ => { _netManager.Update(); });

            _netManager.UdpSendEvents
                .Subscribe(e =>
                {
                    _udpRx.OutgoingUdpMessageSender.Send(e.Message, e.Start, e.Length, e.RemoteEndPoint,
                        UdpSendType.SendTo);
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
                    GameObject clientGameObj = Instantiate(PlayerPrefab);

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

                    IDisposable incomingPacketSub = psRx.GamePacketStream
                        .Select(kccServer.GetClientEventFromStream)
                        .Subscribe(controlledObjEvent => { kccServer.HandleClientEvent(controlledObjEvent); });

                    IDisposable outgoingPacketSub = psRx.OutgoingPacketStream
                        .Subscribe(stream =>
                        {
                            kccServer.WriteToPacketStream(stream);
                            evt.Peer.Send(stream.Data, 0, stream.Length, DeliveryMethod.Unreliable);
                        });

                    IDisposable transmissionNotificationSub = psRx.TransmissionNotificationStream
                        .Subscribe(notification =>
                        {
                            //client.Replication.ReceiveNotification(notification);
                        });

                    // Take(1) ensures this will dispose after the first disconnect event it receives
                    netRx.ReceivedNetEventStream
                        .Where(e => e.Type == NetEvent.EType.Disconnect && e.Peer.Id == evt.Peer.Id)
                        .Take(1)
                        .Do(_ => Debug.Log($"CLIENT: {_.Peer.Id} disconnected!"))
                        .Subscribe(
                            _ =>
                            {
                                Destroy(clientGameObj);
                                transmissionNotificationSub.Dispose();
                                outgoingPacketSub.Dispose();
                                incomingPacketSub.Dispose();
                            });

                    psRx.Start();
                });

            // Start network thread
            _netManager.StartUdpSendEvents();
            _udpRx.Start();
            netRx.Start();
            connGeneratePacketEvents.Connect();
        }
    }
}