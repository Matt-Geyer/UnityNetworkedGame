using LiteNetLib;
using LiteNetLib.Utils;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

[CreateAssetMenu(fileName = "TestPacketStreamReactor", menuName = "TestPacketStreamReactor")]
public class TestPacketStreamReactor : ScriptableNetEventReactor
{
    public Dictionary<int, GameClient> Clients = new Dictionary<int, GameClient>();

    private GameClient Client;

    public override void Initialize(ILogger logger)
    {
        base.Initialize(logger);

        

    }

    public override void React(GameEvent evt)
    {
        if (evt.EventId == GameEvent.Event.NETEVENT)
        {
            React(evt.NetEvent);
        }
        else if (evt.EventId == GameEvent.Event.UPDATE)
        {
            UpdateClients();
        }
    }

    private void UpdateClients()
    {
        foreach (GameClient gc in Clients.Values)
        {
            gc.PacketStream.Update();
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




[CreateAssetMenu(fileName = "GameServerReactor", menuName = "GameServerReactor")]
public class GameServerReactor : ScriptableNetEventReactor
{

    private byte[] buffer;

    public GameObject EntityPrefab;

    public GameObject[] Entities;

    private Stack<int> EntityIds;

    public GameObject ClientPrefab;

    public Dictionary<int, GameClient> Clients;

    private ushort Seq = 0;

    private ushort LastClientSeq = 0;


    public override void Initialize(ILogger logger)
    {
        base.Initialize(logger);

        Entities = new GameObject[100];

        EntityIds = new Stack<int>();

        for (int i = 99; i >= 0; i--)
        {
            EntityIds.Push(i);
        }


        Clients = new Dictionary<int, GameClient>();

        // add a couple -- this is not where this would normally be ofc
        for (int i = 0; i < 10; i++)
            Entities[0] = Instantiate(EntityPrefab, new Vector3(0, i * 10, 0), new Quaternion());

        buffer = new byte[1024];

        for (int i = 0; i < 1024; i++)
        {
            buffer[i] = (byte)i;
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
            UpdateClients();
        }
    }

    private readonly GamePacket packet = new GamePacket { Type = GamePacket.PacketType.SERVERUPDATE, Positions = new Vector3[100] };
    private void UpdateClients()
    {

        packet.PositionCount = 0;
        for (int i = 0; i < Entities.Length; i++)
        {
            if (Entities[i] != null)
            {
                packet.Positions[packet.PositionCount] = Entities[i].transform.position;
                packet.PositionCount++;
            }
        }

        Debug.Log($"Sending the position for {packet.PositionCount} entities");

        packet.Seq = Seq++;

        foreach (NetPeer peer in R_NetManager.ConnectedPeerList)
        {
            GameClient gc = Clients[peer.Id];

            if (gc.CurrentState == GameClient.State.LOADING)
            {
                SendInitToClient(gc);
            }
            else if (gc.CurrentState == GameClient.State.PLAYING)
            {
                packet.LastClientSeq = gc.LastProcessedSequence;

                NetDataWriter writer = new NetDataWriter(false, 600);

                packet.Serialize(writer);

                peer.Send(writer.Data, 0, writer.Length, DeliveryMethod.Unreliable);

            }
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
                HandleNewConnectionWithEntities(evt);
                break;
            case NetEvent.EType.Receive:
                HandleNetworkReceive(evt);
                break;
        }
    }

    private GamePacket t_packet = new GamePacket { Positions = new Vector3[200], UserInput = new UserInputSample { Pressed = new ushort[200] } };

    private void HandleNetworkReceive(NetEvent gameEvent)
    {
        t_packet.Deserialize(gameEvent.DataReader);

        GameClient gc = Clients[gameEvent.Peer.Id];

        switch (t_packet.Type)
        {
            case GamePacket.PacketType.CLIENTUPDATE:
                gc.CurrentState = GameClient.State.PLAYING;
                GameObject playerEntity = Entities[gc.EntityId];

                PlayerController pc = playerEntity.GetComponent<PlayerController>();

                if (t_packet.Seq > LastClientSeq)
                {
                    LastClientSeq = t_packet.Seq;
                    pc.ApplyInput(t_packet.UserInput);
                }
                break;
        }
    }

    private void SendInitToClient(GameClient client)
    {
        GamePacket packet = new GamePacket { Type = GamePacket.PacketType.INIT, EntityId = (ushort)client.EntityId };

        NetDataWriter writer = new NetDataWriter(false, 600);

        packet.Serialize(writer);

        client.Peer.Send(writer.Data, 0, writer.Length, DeliveryMethod.Unreliable);

    }

    private void HandleNewConnectionWithEntities(NetEvent evt)
    {

        Debug.Log("Creating entity");

        EntityManager em = World.Active.EntityManager;

        Entity e = em.CreateEntity();

       
    }

    private void HandleNewConnection(NetEvent evt)
    {
        // Need an entity in the game world..
        int nextEntityId = EntityIds.Pop();
        Entities[nextEntityId] = Instantiate(ClientPrefab);
        GameClient client = new GameClient(evt.Peer)
        {
            CurrentState = GameClient.State.LOADING,
            EntityId = nextEntityId
        };

        Clients[client.Peer.Id] = client;

        // Send ... init packet?
        SendInitToClient(client);
    }
}
