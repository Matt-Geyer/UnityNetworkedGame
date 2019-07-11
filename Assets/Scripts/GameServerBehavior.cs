using System;
using Assets.Scripts.Network;
using LiteNetLib;
using UnityEngine;
using Disposable = UniRx.Disposable;
using UniRx;
using UniRx.Triggers;

namespace Assets.Scripts
{
    public class GameServerBehavior : MonoBehaviour
    {
        private UdpNetworkBehavior _network;

        public GameObject ObjectPrefab;
        public GameObject PlayerPrefab;

        private TimeSpan _timeoutCheckFrequency;

        // Start is called before the first frame update
        private void Start()
        {
            _timeoutCheckFrequency = TimeSpan.FromSeconds(5);

            _network = new UdpNetworkBehavior
            {
                ShouldConnect = false,
                ShouldBind = true
            };

            // Start consuming the net event stream
            GameServerRx reactor =
                new GameServerRx(_network.RNetManager, _network.NetEventStream, ObjectPrefab, PlayerPrefab);
            
            // Start network thread
            _network.Start();
        }
    }
} 