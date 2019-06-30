﻿using UnityEngine;

namespace Assets.Scripts
{
    [RequireComponent(typeof(UdpNetworkBehavior))]
    public class GameClientBehavior : MonoBehaviour
    {
        UdpNetworkBehavior Network;

        public GameObject EntityPrefab;
        public GameObject PlayerPrefab;

        // Start is called before the first frame update
        void Start()
        {
            GameClientReactor reactor = new GameClientReactor
            {
                EntityPrefab = EntityPrefab,
                PlayerPrefab = PlayerPrefab
            };
            Network = GetComponent<UdpNetworkBehavior>();
            Network.R_GameReactor = reactor;
            Network.ShouldBind = false;
            Network.ShouldConnect = true;
            Network.enabled = true;
        }

        // Update is called once per frame
        void Update()
        {
        
        }
    }
}
