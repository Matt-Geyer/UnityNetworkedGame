using Assets.Scripts.Network;
using UnityEngine;

namespace Assets.Scripts
{
    [RequireComponent(typeof(UdpNetworkBehavior))]
    public class GameServerBehavior : MonoBehaviour
    {
        private UdpNetworkBehavior _network;

        public GameObject ObjectPrefab;
        public GameObject PlayerPrefab;

        // Start is called before the first frame update
        private void Start()
        {
            GameServerReactor reactor = new GameServerReactor
            {
                EntityPrefab = ObjectPrefab,
                ClientPrefab = PlayerPrefab
            };

            reactor.Initialize();
            _network = GetComponent<UdpNetworkBehavior>();
            _network.RGameReactor = reactor;
            _network.ShouldConnect = false;
            _network.ShouldBind = true;
            _network.enabled = true;
        }
    }
}