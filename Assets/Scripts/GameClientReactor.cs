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

    public GameClient Client;

    public GameObject[] Entities = new GameObject[100];

    public Dictionary<ushort, GameObject> R_GameObjects = new Dictionary<ushort, GameObject>();

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
                OnNetEvent(evt.NetEvent);
                break;
            case GameEvent.Event.UPDATE:
                OnUpdate();
                break;
        }
    }   

    private void OnUpdate()
    {
        if (CurrentState == State.PLAYING)
        {
            Client.PacketStream.UpdateIncoming();

            // Update game
            foreach (ReplicationRecord r in Client.Replication.ReplicatedObjects.Values)
            {
                if (!R_GameObjects.ContainsKey(r.Id))
                {
                    R_GameObjects[r.Id] = Instantiate(EntityPrefab);
                }
                ReplicatableGameObject rgo = r.Entity as ReplicatableGameObject;
                R_GameObjects[r.Id].transform.SetPositionAndRotation(rgo.Position, new Quaternion());
            }

            Client.PacketStream.UpdateOutgoing();
        }
    }

    private void OnNetEvent(NetEvent evt)
    {
        log.Log($"NetEvent: {evt.Type} ");
        switch (evt.Type)
        {
            case NetEvent.EType.Connect:
                OnConnected(evt);
                break;
            case NetEvent.EType.Receive:
                Client.PacketStream.DataReceivedEvents.Add(evt);
                break;
        }
    }

    private void OnConnected(NetEvent evt)
    {
        log.Log("I'm connected!");
        CurrentState = State.PLAYING;
        Client = new GameClient(evt.Peer);

        GameObject playerGO = Instantiate(PlayerPrefab);

        PlayerControlledObject pco = new PlayerControlledObject { Entity = playerGO, PlayerController = playerGO.GetComponent<CharacterController>() };

        Client.PlayerControlledObjectSys.ControlledObject = pco;

    }



}
