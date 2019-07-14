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

        private UdpNetworkBehavior _network;

        //public GameObject EntityPrefab;
        public GameObject PlayerPrefab;

        private enum FixedUpdateLoopEvents
        {
            Physics,
            Input,
            Reconcile
        }

        // Start is called before the first frame update
        // ReSharper disable once UnusedMember.Local
        private void Start()
        {
            _log = NLogManager.Instance.GetLogger(this);

            KinematicCharacterSystem.AutoSimulation = false;
            KinematicCharacterSystem.Interpolate = false;
            KinematicCharacterSystem.EnsureCreation();

            var connFuEvents = Observable.Create<FixedUpdateLoopEvents>(observer =>
            {
                return Observable.EveryFixedUpdate().Subscribe(_ =>
                {
                    observer.OnNext(FixedUpdateLoopEvents.Input);
                    observer.OnNext(FixedUpdateLoopEvents.Reconcile);
                    observer.OnNext(FixedUpdateLoopEvents.Physics);
                });
            }).Publish();

            var handleInputSignal = connFuEvents.Where(s => s == FixedUpdateLoopEvents.Input);
            var handleReconcileSignal = connFuEvents.Where(s => s == FixedUpdateLoopEvents.Reconcile);
            var handlePhysicsSignal = connFuEvents.Where(s => s == FixedUpdateLoopEvents.Physics);
            
            handlePhysicsSignal.Subscribe(_ =>
            {
                Debug.Log($"***************************** PHYSICS: {Time.frameCount} ***********************");

                // Update physics
                KinematicCharacterSystem.Simulate(
                    Time.fixedDeltaTime,
                    KinematicCharacterSystem.CharacterMotors,
                    KinematicCharacterSystem.CharacterMotors.Count,
                    KinematicCharacterSystem.PhysicsMovers,
                    KinematicCharacterSystem.PhysicsMovers.Count);
            });

            _network = new UdpNetworkBehavior
            {
                ShouldBind = false,
                ShouldConnect = true
            };

            IConnectableObservable<long> connGeneratePacketEvents =
                Observable.EveryUpdate().Sample(TimeSpan.FromSeconds(Time.fixedDeltaTime)).Publish();

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

                    handleInputSignal.Subscribe(_ =>
                    {
                        Debug.Log($"*************** INPUT: {Time.frameCount}  ******************");
                        kccClient.UpdateControlledObject();
                    });

                    Queue<ControlledObjectServerEvent> updateQueue = new Queue<ControlledObjectServerEvent>();

                    handleReconcileSignal.Subscribe(_ =>
                    {
                        Debug.Log($"*************** RECONCILE: {Time.frameCount} -- {updateQueue.Count} SERVER UPDATES******************");
                        for (int i =  updateQueue.Count; i > 0; i--)
                        {
                            kccClient.FixedUpdate_ServerReconcile(updateQueue.Dequeue());
                        }
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
                            for (int i = 0; i < serverUpdates.Count; i++) updateQueue.Enqueue(serverUpdates[i]);
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

            _network.Start();
            netRx.Start();
            connFuEvents.Connect();
            connGeneratePacketEvents.Connect();
        }
    }
}