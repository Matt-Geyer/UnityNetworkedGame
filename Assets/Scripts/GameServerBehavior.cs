using UnityEngine;

namespace Assets.Scripts
{
    [RequireComponent(typeof(UdpNetworkBehavior))]
    public class GameServerBehavior : MonoBehaviour
    {

        public GameObject ObjectPrefab;
        public GameObject PlayerPrefab;

        UdpNetworkBehavior Network;

        // Start is called before the first frame update
        void Start()
        {
            GameServerReactor reactor = new GameServerReactor
            {
                EntityPrefab = ObjectPrefab,
                ClientPrefab = PlayerPrefab
            };

            reactor.Initialize();
            Network = GetComponent<UdpNetworkBehavior>();
            Network.R_GameReactor = reactor;
            Network.ShouldConnect = false;
            Network.ShouldBind = true;
            Network.enabled = true;
        }

        // Update is called once per frame
        void Update()
        {
        
        }
    }
}
