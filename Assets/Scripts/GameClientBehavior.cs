using System.Diagnostics.CodeAnalysis;
using Assets.Scripts.Network;
using UnityEngine;

namespace Assets.Scripts
{
    [RequireComponent(typeof(UdpNetworkBehavior))]
    public class GameClientBehavior : MonoBehaviour
    {
        private UdpNetworkBehavior _network;

        public GameObject EntityPrefab;
        public GameObject PlayerPrefab;

        // Start is called before the first frame update
        private void Start()
        {
            GameClientReactor reactor = new GameClientReactor
            {
                EntityPrefab = EntityPrefab,
                PlayerPrefab = PlayerPrefab
            };
            _network = GetComponent<UdpNetworkBehavior>();
            _network.RGameReactor = reactor;
            _network.ShouldBind = false;
            _network.ShouldConnect = true;
            _network.enabled = true;
        }
    }
}