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
    public sealed class GameClientRx: NetEventReactor
    {
        private State _currentState;

        private GameClient _client;

        public GameObject[] Entities = new GameObject[100];
        public GameObject EntityPrefab;

        private readonly NLogger _log;

        private readonly GameObject _playerPrefab;

        public Dictionary<ushort, GameObject> RGameObjects = new Dictionary<ushort, GameObject>();
        
        public GameClientRx(NetManager netManager, IObservable<NetEvent> eventStream, GameObject entityPrefab, GameObject playerPrefab)
        {
            EntityPrefab = entityPrefab;
            _playerPrefab = playerPrefab;
            RNetManager = netManager;

            KinematicCharacterSystem.AutoSimulation = false;
            KinematicCharacterSystem.Interpolate = false;
            KinematicCharacterSystem.EnsureCreation();

            _currentState = State.Connecting;

            _log = NLogManager.Instance.GetLogger(this);

            // Handle connection events
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
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            });

            Observable.EveryFixedUpdate().Subscribe(_ => Update());
        }

        private void OnReceive(NetEvent evt)
        {
            _client.PacketStream.AddDataReceivedEvent(evt);
        }

        public void Update()
        {
            if (_currentState != State.Playing) return;

            _client.PacketStream.UpdateIncoming();

            _client.ControlledObjectSys.UpdateControlledObject();

            foreach (ReplicationRecord r in _client.Replication.ReplicatedObjects.Values)
            {
                if (!RGameObjects.ContainsKey(r.Id)) RGameObjects[r.Id] = Object.Instantiate(EntityPrefab);
                ReplicatableGameObject rgo = (ReplicatableGameObject)r.Entity;
                RGameObjects[r.Id].transform.SetPositionAndRotation(rgo.Position, new Quaternion());
            }

            // Update game
            KinematicCharacterSystem.Simulate(
                Time.fixedDeltaTime,
                KinematicCharacterSystem.CharacterMotors,
                KinematicCharacterSystem.CharacterMotors.Count,
                KinematicCharacterSystem.PhysicsMovers,
                KinematicCharacterSystem.PhysicsMovers.Count);

            _client.PacketStream.UpdateOutgoing();
        }

        private void OnConnect(NetEvent evt)
        {
      
            _currentState = State.Playing;
            _client = new GameClient(evt.Peer, false);

            GameObject playerObj = Object.Instantiate(_playerPrefab);

            KccControlledObject pco = new KccControlledObject
            {
                Entity = playerObj,
                PlayerController = playerObj.GetComponent<CharacterController>(),
                Controller = playerObj.GetComponent<MyCharacterController>()
            };

            pco.Controller.Motor.SetPosition(new Vector3(0, 2, 0));

            _client.ControlledObjectSys.CurrentlyControlledObject = pco;
        }

        private enum State
        {
            Connecting,
            Ready,
            Playing
        }
    }
}