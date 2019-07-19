using System;
using AiUnity.NLog.Core;
using Animancer;
using Assets.Scripts.Network;
using Cinemachine;
using LiteNetLib;
using UnityEngine;

namespace Assets.Scripts
{
    // ReSharper disable once UnusedMember.Global
    public class GameClientBehavior : MonoBehaviour
    {
        private NLogger _log;
        private NetManager _netManager;
        private TimeSpan _timeoutCheckFrequency;
        private UdpServer _udpRx;

        public int BindPort = 50069;

        /// <summary>
        ///     The host to connect to
        /// </summary>
        public string ConnectAddress = "127.0.0.1";

        /// <summary>
        ///     The port to connect to
        /// </summary>
        public int ConnectPort = 40069;

        public int CheckTimeoutFrequencySeconds = 1;

        //public GameObject EntityPrefab;
        public GameObject PlayerPrefab;

        public void Start()
        {
            _gameClientRx = new GameClientRx(new GameClientRxOptions
            {
                ConnectAddress = ConnectAddress,
                ConnectPort = ConnectPort,
                BindPort = BindPort,
                CheckTimeoutFrequencySeconds = CheckTimeoutFrequencySeconds,
                Camera = _camera,
                WalkBlendTree = _walkBlendTree,
                PlayerPrefab = PlayerPrefab
            });
        }


        private void OnDestroy()
        {
           _gameClientRx.Stop();
        }

        private enum FixedUpdateLoopEvents
        {
            Physics,
            Input,
            Reconcile
        }

#pragma warning disable 649
        [SerializeField] private CinemachineVirtualCamera _camera;
        [SerializeField] private FloatControllerState.Serializable _walkBlendTree;
        private GameClientRx _gameClientRx;
#pragma warning restore 649
    }
}