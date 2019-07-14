using System;
using System.Collections.Generic;
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

        private NLogger _log;
        private NetManager _netManager;
        private TimeSpan _timeoutCheckFrequency;
        private UdpServer _udpRx;

        /// <summary>
        ///     The host to connect to
        /// </summary>
        public string ConnectAddress = "127.0.0.1";

        /// <summary>
        ///     The port to connect to
        /// </summary>
        public int ConnectPort = 40069;

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

            physicsFixedUpdateEvents.Subscribe(_ =>
            {
                //Debug.Log($"***************************** PHYSICS: {Time.frameCount} ***********************");

                // Update physics
                KinematicCharacterSystem.Simulate(
                    Time.fixedDeltaTime,
                    KinematicCharacterSystem.CharacterMotors,
                    KinematicCharacterSystem.CharacterMotors.Count,
                    KinematicCharacterSystem.PhysicsMovers,
                    KinematicCharacterSystem.PhysicsMovers.Count);
            });

            _udpRx = new UdpServer
            {
                BindPort = 9090
            };

            IConnectableObservable<long> connGeneratePacketEvents =
                Observable.EveryUpdate().Sample(TimeSpan.FromSeconds(Time.fixedDeltaTime)).Publish();

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

                    inputFixedUpdateEvents.Subscribe(_ => { kccClient.UpdateControlledObject(); });

                    Queue<ControlledObjectServerEvent> updateQueue = new Queue<ControlledObjectServerEvent>();

                    reconcileFixedUpdateEvents.Subscribe(_ =>
                    {
                        for (int i = updateQueue.Count; i > 0; i--)
                            kccClient.FixedUpdate_ServerReconcile(updateQueue.Dequeue());
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
                            kccClient.WriteToPacketStream(stream);
                            _client.Peer.Send(stream.Data, 0, stream.Length, DeliveryMethod.Unreliable);
                        });

                    psRx.TransmissionNotificationStream
                        .Subscribe(notification =>
                        {
                            //client.Replication.ReceiveNotification(notification);
                        });

                    psRx.Start();
                });


            _netManager.StartUdpSendEvents();
            _udpRx.Start();
            netRx.Start();
            connFixedUpdateEventStream.Connect();
            connGeneratePacketEvents.Connect();
        }

        private enum FixedUpdateLoopEvents
        {
            Physics,
            Input,
            Reconcile
        }
    }
}