using UnityEngine;

namespace Assets.Scripts
{
    // ReSharper disable once UnusedMember.Global
    public class GameServerBehavior : MonoBehaviour
    {
        /// <summary>
        ///     The local endpoint address to bind to
        /// </summary>
        public string BindAddress = "0.0.0.0";

        /// <summary>
        ///     The local endpoint port to bind the socket to
        /// </summary>
        public int BindPort = 40069;

        /// <summary>
        ///     How often the CheckTimeout logic should run
        /// </summary>
        public float CheckTimeoutFrequencySeconds = 1.0f;

        //public GameObject ObjectPrefab;

        public GameObject PlayerPrefab;

        private GameServerRx _gameServer;

        // Start is called before the first frame update
        // ReSharper disable once UnusedMember.Local
        private void Start()
        {
            _gameServer = new GameServerRx(new GameServerRxOptions
            {
                BindPort = BindPort,
                BindAddress = BindAddress,
                CheckTimeoutFrequencySeconds = CheckTimeoutFrequencySeconds,
                PlayerPrefab = PlayerPrefab
            });
        }

        // ReSharper disable once UnusedMember.Local
        private void OnDestroy()
        {
            _gameServer.Stop();
        }

    }
}