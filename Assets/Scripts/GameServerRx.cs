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
using Object = UnityEngine.Object;

namespace Assets.Scripts
{
    public class GameServerRx : NetEventReactor
    {
        private readonly Stack<int> _entityIds;
        private readonly NLogger _log;


        private readonly GameObject _clientPrefab;

        public Dictionary<int, GameClient> Clients;

        public GameObject[] Entities;

        public ReplicatableGameObject[] REntities;

        public GameServerRx(NetManager netManager, IObservable<NetEvent> eventStream, GameObject entityPrefab, GameObject clientPrefab)
        {
            _log = NLogManager.Instance.GetLogger(this);
            _clientPrefab = clientPrefab;
            RNetManager = netManager;

            Entities = new GameObject[100];
            REntities = new ReplicatableGameObject[100];

            _entityIds = new Stack<int>();

            for (int i = 99; i >= 0; i--) _entityIds.Push(i);

            Clients = new Dictionary<int, GameClient>();

            KinematicCharacterSystem.AutoSimulation = false;
            KinematicCharacterSystem.Interpolate = false;
            KinematicCharacterSystem.EnsureCreation();

            // add a couple -- this is not where this would normally be ofc
            for (int i = 0; i < 10; i++)
            {
                Entities[i] = Object.Instantiate(entityPrefab, new Vector3(0, (i + 1) * 10, 0), new Quaternion());
                REntities[i] = new ReplicatableGameObject();
            }

            // React to the NetEvent stream
            eventStream.Subscribe(evt =>
            {
                switch (evt.Type)
                {
                    case NetEvent.EType.Connect:
                        OnConnect(evt);
                        break;
                    case NetEvent.EType.Disconnect:
                        break;
                    case NetEvent.EType.Receive:
                        OnReceive(evt);
                        break;
                    case NetEvent.EType.ReceiveUnconnected:
                        break;
                    case NetEvent.EType.Error:
                        break;
                    case NetEvent.EType.ConnectionLatencyUpdated:
                        break;
                    case NetEvent.EType.DiscoveryRequest:
                        break;
                    case NetEvent.EType.DiscoveryResponse:
                        break;
                    case NetEvent.EType.ConnectionRequest:
                        OnConnectionRequest(evt);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            });

            Observable.EveryFixedUpdate().Subscribe(_ => Update());
        }

        private static void OnConnectionRequest(NetEvent evt)
        {
            Debug.Log("ACCEPTING CONNECTION REQUEST!");
            evt.ConnectionRequest.Accept();
        }


        private void Update()
        {
            _log.Debug($"{Time.frameCount}: STARTED UPDATE");
            
            Clients_UpdateIncomingPacketStream();

            if (KinematicCharacterSystem.CharacterMotors.Count > 0)
            {
                // update game
                KinematicCharacterSystem.Simulate(
                    Time.fixedDeltaTime,
                    KinematicCharacterSystem.CharacterMotors,
                    KinematicCharacterSystem.CharacterMotors.Count,
                    KinematicCharacterSystem.PhysicsMovers,
                    KinematicCharacterSystem.PhysicsMovers.Count);
            }
            
            for (int i = 0; i < 10; i++)
            {
                REntities[i].Position = Entities[i].transform.position;

                // detect changes by comparing previous iterations values
                REntities[i].UpdateStateMask();
            }

            Clients_UpdateOutgoingPacketStream();

            _log.Debug($"{Time.frameCount}: FINISHED UPDATE");
        }

        private void Clients_UpdateIncomingPacketStream()
        {
            foreach (GameClient gc in Clients.Values)
            {
                if (gc.CurrentState == GameClient.State.Playing) gc.PacketStream.UpdateIncoming(true);
            }
        }

        private void Clients_UpdateOutgoingPacketStream()
        {
            foreach (GameClient gc in Clients.Values) gc.PacketStream.UpdateOutgoing(true);
        }
        
        private void OnReceive(NetEvent evt)
        {
            GameClient gc = Clients[evt.Peer.Id];

            gc.PacketStream.AddDataReceivedEvent(evt);
        }

        private void OnConnect(NetEvent evt)
        {
            // Need an entity in the game world..
            int nextEntityId = _entityIds.Pop();
            //Entities[nextEntityId] = Instantiate(ClientPrefab);
            GameClient client = new GameClient(evt.Peer, true)
            {
                CurrentState = GameClient.State.Playing,
                EntityId = nextEntityId
            };

            GameObject clientGameObj = Object.Instantiate(_clientPrefab);

            KccControlledObject kcc = new KccControlledObject
            {
                Entity = clientGameObj,
                PlayerController = clientGameObj.GetComponent<CharacterController>(),
                Controller = clientGameObj.GetComponent<MyCharacterController>()
            };

            kcc.Controller.Motor.SetPosition(new Vector3(0, 2, 0));

            client.ControlledObjectSys.CurrentlyControlledObject = kcc;

            Clients[client.Peer.Id] = client;

            for (int i = 0; i < 10; i++) client.Replication.StartReplicating(REntities[i]);

         
        }
    }
}