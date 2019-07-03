using System;
using System.Collections.Generic;
using AiUnity.NLog.Core;
using Assets.Scripts.CharacterControllerStuff;
using Assets.Scripts.Network;
using Assets.Scripts.Network.StreamSystems;
using LiteNetLib;
using Opsive.UltimateCharacterController.Character;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Assets.Scripts
{
    public class GameServerReactor : NetEventReactor
    {
        private readonly Stack<int> _entityIds;
        private readonly NLogger _log;


        public GameObject ClientPrefab;

        public Dictionary<int, GameClient> Clients;

        public GameObject[] Entities;

        public GameObject EntityPrefab;

        public ReplicatableGameObject[] REntities;

        public GameServerReactor()
        {
            _log = NLogManager.Instance.GetLogger(this);

            Entities = new GameObject[100];
            REntities = new ReplicatableGameObject[100];

            _entityIds = new Stack<int>();

            for (int i = 99; i >= 0; i--) _entityIds.Push(i);

            Clients = new Dictionary<int, GameClient>();
        }

        public void Initialize()
        {
            // add a couple -- this is not where this would normally be ofc
            for (int i = 0; i < 10; i++)
            {
                Entities[i] = Object.Instantiate(EntityPrefab, new Vector3(0, (i + 1) * 10, 0), new Quaternion());
                REntities[i] = new ReplicatableGameObject();
            }
        }

        public override void React(GameEvent evt)
        {
            switch (evt.EventId)
            {
                case GameEvent.Event.NetEvent:
                    React(evt.NetEvent);
                    break;
                case GameEvent.Event.Update:
                    OnUpdate();
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private void OnUpdate()
        {
            _log.Debug($"{Time.frameCount}: STARTED UPDATE");

            Clients_UpdateIncomingPacketStream();



            // update game


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
            foreach (NetPeer peer in RNetManager.ConnectedPeerList)
            {
                GameClient gc = Clients[peer.Id];

                if (gc.CurrentState == GameClient.State.Playing) gc.PacketStream.UpdateIncoming(true);
            }
        }

        private void Clients_UpdateOutgoingPacketStream()
        {
            foreach (NetPeer peer in RNetManager.ConnectedPeerList) Clients[peer.Id].PacketStream.UpdateOutgoing(true);
        }

        private void React(NetEvent evt)
        {
            switch (evt.Type)
            {
                case NetEvent.EType.ConnectionRequest:
                    evt.ConnectionRequest.Accept(); // who needs security
                    break;
                case NetEvent.EType.Connect:
                    HandleNewConnection(evt);
                    break;
                case NetEvent.EType.Receive:
                    HandleNetworkReceive(evt);
                    break;
                case NetEvent.EType.Disconnect:
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
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private void HandleNetworkReceive(NetEvent evt)
        {
            GameClient gc = Clients[evt.Peer.Id];

            gc.PacketStream.AddDataReceivedEvent(evt);
        }

        private void HandleNewConnection(NetEvent evt)
        {
            // Need an entity in the game world..
            int nextEntityId = _entityIds.Pop();
            //Entities[nextEntityId] = Instantiate(ClientPrefab);
            GameClient client = new GameClient(evt.Peer, true)
            {
                CurrentState = GameClient.State.Playing,
                EntityId = nextEntityId
            };

            GameObject clientGameObj = Object.Instantiate(ClientPrefab);

            client.ControlledObjectSys.CurrentlyControlledObject = new UccControlledObject
            {
                Entity = clientGameObj,
                PlayerController = clientGameObj.GetComponent<CharacterController>(),
                PLocomotion =  clientGameObj.GetComponent<UltimateCharacterLocomotion>()
            };

            Clients[client.Peer.Id] = client;

            for (int i = 0; i < 10; i++) client.Replication.StartReplicating(REntities[i]);

            _log.Debug("Got new connection!");
        }
    }
}