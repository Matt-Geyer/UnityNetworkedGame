using LiteNetLib;
using LiteNetLib.Utils;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;


[CreateAssetMenu(fileName = "GameClientReactor", menuName = "GameClientReactor")]
public class GameClientReactor : ScriptableNetEventReactor
{
    public GameObject EntityPrefab;

    public GameObject PlayerPrefab;

    public PlayerController PC;

    public GameObject[] Entities = new GameObject[100];

    public int MyEntityId = 0;

    public List<UserInputSample> UserInputSamples = new List<UserInputSample>();

    public ushort UserInputSeq = 0;

    private enum State
    {
        CONNECTING,
        READY,
        PLAYING
    }

    private State CurrentState;

    public override void Initialize(ILogger logger)
    {
        base.Initialize(logger);

        CurrentState = State.CONNECTING;

    }


    public override void React(GameEvent evt)
    {
        switch (evt.EventId)
        {
            case GameEvent.Event.NETEVENT:
                React(evt.NetEvent);
                break;
            case GameEvent.Event.UPDATE:
                Update();
                break;
        }
    }

    private readonly GamePacket tempPacket = new GamePacket { 
        Type = GamePacket.PacketType.CLIENTUPDATE,
        Positions = new Vector3[200] };

    private void Update()
    {
        if (CurrentState != State.PLAYING) { return; }

        Debug.Log("Sampling Input..");

        // sample input
        var input = new UserInputSample { Seq = UserInputSeq++, Pressed = new ushort[2] };
        UserInputUtils.Sample(input);
        UserInputSamples.Add(input);

        Debug.Log("Applying input");
        // apply input
        PC.ApplyInput(input);

        tempPacket.Type = GamePacket.PacketType.CLIENTUPDATE;
        tempPacket.UserInput = input;
        NetDataWriter writer = new NetDataWriter(false, 1000);
        tempPacket.Serialize(writer);

        Debug.Log("About to send!");

        // update server
        //R_NetManager.ConnectedPeerList[0].Send(writer.Data, 0, writer.Length, DeliveryMethod.Unreliable);
    }

   

    private void React(NetEvent evt)
    {
        log.Log($"NetEvent: {evt.Type} ");
        switch (evt.Type)
        {
            case NetEvent.EType.Connect:
                log.Log("I'm connected!");
                CurrentState = State.READY;
                break;
            case NetEvent.EType.Receive:
                tempPacket.Deserialize(evt.DataReader);
                React(tempPacket);
                break;
        }
    }

    private void React(GamePacket gameEvent)
    {
        switch (gameEvent.Type)
        {
            case GamePacket.PacketType.INIT:
                if (CurrentState == State.READY)
                {
                    Debug.Log("CLIENT INIT");
                    MyEntityId = gameEvent.EntityId;
                    Entities[MyEntityId] = Instantiate(PlayerPrefab);
                    PC = Entities[MyEntityId].GetComponent<PlayerController>();
                    CurrentState = State.PLAYING;
                }
              
       
                break;
            case GamePacket.PacketType.SERVERUPDATE:
                OnGameUpdate(gameEvent);
                break;

        }
    }

    private void OnGameUpdate(GamePacket updatePacket)
    {
        Debug.Log($"Packet contains {updatePacket.PositionCount} entities!");

        for (int i = 0; i < updatePacket.PositionCount; i++)
        {
            if (i == MyEntityId)
            {
                // server reconciliation 
                if (Entities[i] == null)
                {
                    Debug.Log("INSTANTIATING MY ENTITY");
                    Entities[i] = Instantiate(PlayerPrefab);
                    PC = Entities[i].GetComponent<PlayerController>();
                    CurrentState = State.PLAYING;
                }
                Debug.Log("UPDATING MY ENTITY");
                Entities[i].transform.SetPositionAndRotation(updatePacket.Positions[i], Entities[i].transform.rotation);
                int deleteCount = 0;
                for (int j = 0; j < UserInputSamples.Count; i++)
                {
                    if (UserInputSamples[j].Seq <= updatePacket.LastClientSeq)
                    {
                        deleteCount++;
                    }
                    else
                    {
                        Debug.Log($"Applying sampled input {updatePacket.PositionCount}!");
                        PC.ApplyInput(UserInputSamples[j]);
                    }
                }
                if (deleteCount > 0)
                {
                    Debug.Log($"Deleting sampled inputs 0 - {deleteCount}!");
                    UserInputSamples.RemoveRange(0, deleteCount);
                }
            }
            else
            {
                if (Entities[i] == null)
                {
                    Entities[i] = Instantiate(EntityPrefab);
                }

                Entities[i].transform.SetPositionAndRotation(updatePacket.Positions[i], Entities[i].transform.rotation);
            }
        }
    }


}
