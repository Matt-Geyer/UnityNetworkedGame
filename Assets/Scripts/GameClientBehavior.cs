using System;
using System.Diagnostics.CodeAnalysis;
using Assets.Scripts.Network;
using UniRx;
using UnityEngine;

namespace Assets.Scripts
{
    public class GameClientBehavior : MonoBehaviour
    {
        private UdpNetworkBehavior _network;

        public GameObject EntityPrefab;
        public GameObject PlayerPrefab;


        // Start is called before the first frame update
        private void Start()
        {
            _network = new UdpNetworkBehavior
            {
                ShouldBind = false,
                ShouldConnect = true
            };

            GameClientRx reactor =
                new GameClientRx(_network.RNetManager, _network.NetEventStream, EntityPrefab, PlayerPrefab);

            _network.Start();
        }
    }
}