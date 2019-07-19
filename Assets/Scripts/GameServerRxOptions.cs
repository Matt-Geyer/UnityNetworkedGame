using UnityEngine;

namespace Assets.Scripts
{
    public sealed class GameServerRxOptions
    {
        public string BindAddress;

        public int BindPort;

        public float CheckTimeoutFrequencySeconds;

        public GameObject PlayerPrefab;
    }
}