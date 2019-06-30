using System.Collections.Generic;
using AiUnity.NLog.Core;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

namespace Assets.Scripts
{
    public class TestPacketStreamReactor : NetEventReactor
    {
        private GameClient Client;
        public Dictionary<int, GameClient> Clients = new Dictionary<int, GameClient>();


        public override void React(GameEvent evt)
        {
            if (evt.EventId == GameEvent.Event.NETEVENT)
                React(evt.NetEvent);
            else if (evt.EventId == GameEvent.Event.UPDATE) UpdateClients();
        }

        private void UpdateClients()
        {
            foreach (GameClient gc in Clients.Values)
            {
                gc.PacketStream.UpdateIncoming();
                gc.PacketStream.UpdateOutgoing();
            }
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
            }
        }

        private void HandleNetworkReceive(NetEvent evt)
        {
            Debug.Log($"RECEIVED NETWORK EVENT FROM PEER: {evt.Peer.Id}!");

            if (Clients.Count == 0)
            {
                Client = Client ?? new GameClient(evt.Peer);

                Client.PacketStream.DataReceivedEvents.Add(evt);
            }
            else
            {
                Clients[evt.Peer.Id].PacketStream.DataReceivedEvents.Add(evt);
            }
        }

        private void HandleNewConnection(NetEvent evt)
        {
            Debug.Log("GOT NEW CONNECTION!");
            Debug.Log($"PeerId: {evt.Peer.Id}");
            Clients[evt.Peer.Id] = new GameClient(evt.Peer);
        }
    }


    public class GameServerReactor : NetEventReactor
    {
        private readonly Stack<int> EntityIds;
        private readonly NLogger Log;

        private byte[] buffer;

        public GameObject ClientPrefab;

        public Dictionary<int, GameClient> Clients;

        public GameObject[] Entities;

        public GameObject EntityPrefab;

        public ReplicatableGameObject[] R_Entities;

        public GameServerReactor()
        {
            Log = NLogManager.Instance.GetLogger(this);

            Entities = new GameObject[100];
            R_Entities = new ReplicatableGameObject[100];

            EntityIds = new Stack<int>();

            for (int i = 99; i >= 0; i--) EntityIds.Push(i);

            Clients = new Dictionary<int, GameClient>();


            buffer = new byte[1024];
        }

        public override void Initialize()
        {
            base.Initialize();

            // add a couple -- this is not where this would normally be ofc
            for (int i = 0; i < 10; i++)
            {
                Entities[i] = Object.Instantiate(EntityPrefab, new Vector3(0, (i + 1) * 10, 0), new Quaternion());
                R_Entities[i] = new ReplicatableGameObject();
            }
        }

        public override void React(GameEvent evt)
        {
            if (evt.EventId == GameEvent.Event.NETEVENT)
            {
                React(evt.NetEvent);
            }
            else if (evt.EventId == GameEvent.Event.UPDATE)
            {
                Log.Debug($"{Time.frameCount}: STARTED UPDATE");

                Clients_UpdateIncomingPacketStream();

                // update game
                for (int i = 0; i < 10; i++)
                {
                    R_Entities[i].Position = Entities[i].transform.position;

                    // detect changes by comparing previous iterations values
                    R_Entities[i].UpdateStateMask();
                }

                Clients_UpdateOutgoingPacketStream();

                Log.Debug($"{Time.frameCount}: FINISHED UPDATE");
            }
        }

        private void Clients_UpdateIncomingPacketStream()
        {
            foreach (NetPeer peer in R_NetManager.ConnectedPeerList)
            {
                GameClient gc = Clients[peer.Id];

                if (gc.CurrentState == GameClient.State.PLAYING) gc.PacketStream.UpdateIncoming(true);
            }
        }

        private void Clients_UpdateOutgoingPacketStream()
        {
            foreach (NetPeer peer in R_NetManager.ConnectedPeerList)
            {
                GameClient gc = Clients[peer.Id];

                if (gc.CurrentState == GameClient.State.LOADING)
                    SendInitToClient(gc);
                else if (gc.CurrentState == GameClient.State.PLAYING) gc.PacketStream.UpdateOutgoing(true);
            }
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
            }
        }

        private void HandleNetworkReceive(NetEvent evt)
        {
            GameClient gc = Clients[evt.Peer.Id];

            gc.PacketStream.DataReceivedEvents.Add(evt);
        }

        private void SendInitToClient(GameClient client)
        {
            GamePacket packet = new GamePacket {Type = GamePacket.PacketType.INIT, EntityId = (ushort) client.EntityId};

            NetDataWriter writer = new NetDataWriter(false, 600);

            packet.Serialize(writer);

            client.Peer.Send(writer.Data, 0, writer.Length, DeliveryMethod.Unreliable);
        }

        private void HandleNewConnection(NetEvent evt)
        {
            // Need an entity in the game world..
            int nextEntityId = EntityIds.Pop();
            //Entities[nextEntityId] = Instantiate(ClientPrefab);
            GameClient client = new GameClient(evt.Peer)
            {
                CurrentState = GameClient.State.PLAYING,
                EntityId = nextEntityId
            };

            GameObject clientGameObj = Object.Instantiate(ClientPrefab);

            client.PlayerControlledObjectSys.ControlledObject = new PlayerControlledObject
            {
                Entity = clientGameObj,
                PlayerController = clientGameObj.GetComponent<CharacterController>()
            };

            Clients[client.Peer.Id] = client;

            for (int i = 0; i < 10; i++) client.Replication.StartReplicating(R_Entities[i]);

            Log.Debug("Got new connection!");


            // Send ... init packet?
            //SendInitToClient(client);
        }
    }
}