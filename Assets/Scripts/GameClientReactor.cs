﻿using System;
using System.Collections.Generic;
using AiUnity.NLog.Core;
using Assets.Scripts.CharacterControllerStuff;
using Assets.Scripts.Network;
using Assets.Scripts.Network.StreamSystems;
using KinematicCharacterController;
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
            KinematicCharacterSystem.AutoSimulation = false;
            KinematicCharacterSystem.Interpolate = false;
            KinematicCharacterSystem.EnsureCreation();

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

            foreach (ReplicationRecord r in Client.Replication.ReplicatedObjects.Values)
            {
                if (!RGameObjects.ContainsKey(r.Id)) RGameObjects[r.Id] = Object.Instantiate(EntityPrefab);
                ReplicatableGameObject rgo = (ReplicatableGameObject)r.Entity;
                RGameObjects[r.Id].transform.SetPositionAndRotation(rgo.Position, new Quaternion());
            }

            //KinematicCharacterSystem.PreSimulationInterpolationUpdate(Time.fixedDeltaTime);

            // Update game
            KinematicCharacterSystem.Simulate(
                Time.fixedDeltaTime,
                KinematicCharacterSystem.CharacterMotors,
                KinematicCharacterSystem.CharacterMotors.Count,
                KinematicCharacterSystem.PhysicsMovers,
                KinematicCharacterSystem.PhysicsMovers.Count);

            //KinematicCharacterSystem.PostSimulationInterpolationUpdate(Time.fixedDeltaTime);

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
                    Client.PacketStream.AddDataReceivedEvent(evt);
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
            Client = new GameClient(evt.Peer, false);

            GameObject playerObj = Object.Instantiate(PlayerPrefab);

            var pco = new KccControlledObject
            {
                Entity = playerObj,
                PlayerController = playerObj.GetComponent<CharacterController>(),
                Controller = playerObj.GetComponent<MyCharacterController>()
            };

            pco.Controller.Motor.SetPosition(new Vector3(0, 2, 0));

            Debug.Log($"PCO.Controller: {pco.Controller}  {pco.Controller.Motor}");


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