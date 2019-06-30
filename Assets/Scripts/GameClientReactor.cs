using System.Collections.Generic;
using AiUnity.NLog.Core;
using LiteNetLib;
using UnityEngine;

namespace Assets.Scripts
{
    public class GameClientReactor : NetEventReactor
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

        public NLogger Log;

        private enum State
        {
            CONNECTING,
            READY,
            PLAYING
        }

        private State CurrentState;

        public GameClientReactor()
        {
            CurrentState = State.CONNECTING;

            Log = NLogManager.Instance.GetLogger(this);
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

                Client.PlayerControlledObjectSys.UpdateControlledObject();

                // Update game
                foreach (ReplicationRecord r in Client.Replication.ReplicatedObjects.Values)
                {
                    if (!R_GameObjects.ContainsKey(r.Id))
                    {
                        R_GameObjects[r.Id] = GameObject.Instantiate(EntityPrefab);
                    }
                    ReplicatableGameObject rgo = r.Entity as ReplicatableGameObject;
                    R_GameObjects[r.Id].transform.SetPositionAndRotation(rgo.Position, new Quaternion());
                }

                Client.PacketStream.UpdateOutgoing();
            }
        }

        private void OnNetEvent(NetEvent evt)
        {
            Log.Debug($"NetEvent: {evt.Type} ");
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
            Log.Debug("I'm connected!");
            CurrentState = State.PLAYING;
            Client = new GameClient(evt.Peer);

            GameObject playerGO = GameObject.Instantiate(PlayerPrefab);

            PlayerControlledObject pco = new PlayerControlledObject { Entity = playerGO, PlayerController = playerGO.GetComponent<CharacterController>() };

            Client.PlayerControlledObjectSys.ControlledObject = pco;

        }



    }
}
