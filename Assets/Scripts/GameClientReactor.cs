using System;
using System.Collections.Generic;
using AiUnity.NLog.Core;
using Assets.Scripts.Network;
using LiteNetLib;
using Opsive.UltimateCharacterController.Character;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Assets.Scripts
{
    public class GameClientReactor : NetEventReactor
    {
        private State _currentState;

        public GameClient Client;

        public GameObject[] Entities = new GameObject[100];
        public GameObject EntityPrefab;

        public NLogger Log;

        public GameObject PlayerPrefab;

        public Dictionary<ushort, GameObject> RGameObjects = new Dictionary<ushort, GameObject>();

        public GameClientReactor()
        {
            _currentState = State.Connecting;

            Log = NLogManager.Instance.GetLogger(this);
        }


        public override void React(GameEvent evt)
        {
            switch (evt.EventId)
            {
                case GameEvent.Event.NetEvent:
                    OnNetEvent(evt.NetEvent);
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
            if (_currentState != State.Playing) return;

            Client.PacketStream.UpdateIncoming();

            Client.ControlledObjectSys.UpdateControlledObject();

            // Update game
            foreach (ReplicationRecord r in Client.Replication.ReplicatedObjects.Values)
            {
                if (!RGameObjects.ContainsKey(r.Id)) RGameObjects[r.Id] = Object.Instantiate(EntityPrefab);
                ReplicatableGameObject rgo = (ReplicatableGameObject) r.Entity;
                RGameObjects[r.Id].transform.SetPositionAndRotation(rgo.Position, new Quaternion());
            }

            Client.PacketStream.UpdateOutgoing();
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
                case NetEvent.EType.ConnectionRequest:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private void OnConnected(NetEvent evt)
        {
            Log.Debug("I'm connected!");
            _currentState = State.Playing;
            Client = new GameClient(evt.Peer, isServer:false);

            GameObject playerObj = Object.Instantiate(PlayerPrefab);

            ControlledObject pco = new ControlledObject
                {Entity = playerObj, PlayerController = playerObj.GetComponent<CharacterController>(), PLocomotion = playerObj.GetComponent<UltimateCharacterLocomotion>()};

            Client.ControlledObjectSys.CurrentlyControlledObject = pco;
        }

        private enum State
        {
            Connecting,
            Ready,
            Playing
        }
    }
}