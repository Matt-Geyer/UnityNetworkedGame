using System;
using AiUnity.NLog.Core;
using Assets.Scripts.Network;
using LiteNetLib;
using UnityEngine;
using UniRx;
using Assets.Scripts.CharacterControllerStuff;
using Assets.Scripts.Network.StreamSystems;
using KinematicCharacterController;


namespace Assets.Scripts
{
    public class GameServerBehavior : MonoBehaviour
    {
        private UdpNetworkBehavior _network;

        public GameObject ObjectPrefab;
        public GameObject PlayerPrefab;

        private TimeSpan _timeoutCheckFrequency;

        private NLogger _log;
        // Start is called before the first frame update
        private void Start()
        {
            _log = NLogManager.Instance.GetLogger(this);
            _timeoutCheckFrequency = TimeSpan.FromSeconds(5);

            _network = new UdpNetworkBehavior
            {
                ShouldConnect = false,
                ShouldBind = true
            };


            KinematicCharacterSystem.AutoSimulation = false;
            KinematicCharacterSystem.Interpolate = false;
            KinematicCharacterSystem.EnsureCreation();

            Observable.EveryFixedUpdate().Subscribe(_ =>
            {
                if (KinematicCharacterSystem.CharacterMotors.Count > 0)
                {
                    // update kinematic character physics
                    KinematicCharacterSystem.Simulate(
                        Time.fixedDeltaTime,
                        KinematicCharacterSystem.CharacterMotors,
                        KinematicCharacterSystem.CharacterMotors.Count,
                        KinematicCharacterSystem.PhysicsMovers,
                        KinematicCharacterSystem.PhysicsMovers.Count);
                }
            });

            IConnectableObservable<long> connGeneratePacketEvents =
                Observable.EveryLateUpdate().Sample(TimeSpan.FromSeconds(Time.fixedDeltaTime)).Publish();

            IObservable<long> generatePacketEvents = connGeneratePacketEvents.RefCount();

            NetManagerRx netRx = new NetManagerRx(_network.RNetManager, _network.UdpMessageStream);

            IObservable<NetEvent> receivedDataStream = netRx.ReceivedNetEventStream
                .Where(evt => evt.Type == NetEvent.EType.Receive)
                .Do(_ => _log.Debug($"Received data: {_.DataReader.RawDataSize}")); 

            netRx.ReceivedNetEventStream
                .Where(evt => evt.Type == NetEvent.EType.ConnectionRequest)
                .Do(_ => _log.Info("Got connection request."))
                .Subscribe(evt => evt.ConnectionRequest.Accept());

            netRx.ReceivedNetEventStream
                .Where(evt => evt.Type == NetEvent.EType.Connect)
                .Do(evt => _log.Info("Client Connected."))
                .Subscribe(evt =>
                {
                    GameClient client = new GameClient(evt.Peer, true)
                    {
                        CurrentState = GameClient.State.Playing
                    };

                    GameObject clientGameObj = Instantiate(PlayerPrefab);

                    KccControlledObject kcc = new KccControlledObject
                    {
                        Entity = clientGameObj,
                        PlayerController = clientGameObj.GetComponent<CharacterController>(),
                        Controller = clientGameObj.GetComponent<MyCharacterController>()
                    };

                    kcc.Controller.Motor.SetPosition(new Vector3(0, 2, 0));

                    client.ControlledObjectSys.CurrentlyControlledObject = kcc;
                    
                    // could add this to a merge that the server subscribes to which groups all the client events 

                    PacketStreamRx psRx = new PacketStreamRx(
                        receivedDataStream.Where(e => e.Peer.Id == evt.Peer.Id), 
                        Observable.EveryUpdate(),
                        generatePacketEvents);

                    // The order of these subscription sort of matters, or at least has implications that are sort of hidden 
                    // by this packet stream reactor.. so i might need to pull that out 
                    psRx.GamePacketStream
                        .Do(_ => _log.Debug("Received game packet stream event"))
                        .Subscribe(stream =>
                    {
                        client.ControlledObjectSys.ReadPacketStream(stream);
                        //client.Replication.ReadPacketStream(stream);
                    });

                    psRx.OutgoingPacketStream
                        .Do(_ => _log.Debug("Writing to packet stream"))
                        .Subscribe(stream =>
                    {
                        client.ControlledObjectSys.WriteToPacketStream(stream);
                        client.Peer.Send(stream.Data, 0, stream.Length, DeliveryMethod.Unreliable);
                    });

                    psRx.TransmissionNotificationStream
                        .Do(not => _log.Debug($"Next notification: {not}"))
                        .Subscribe(notification =>
                        {
                            //client.Replication.ReceiveNotification(notification);
                        });

                    psRx.Start();
                });

            //GameServerRx reactor =
            //    new GameServerRx(_network.RNetManager, _network.NetEventStream, ObjectPrefab, PlayerPrefab);

            // Start network thread
            _network.Start();
            netRx.Start();

            connGeneratePacketEvents.Connect();
        }


    }
} 