using Animancer;
using Cinemachine;
using UnityEngine;

namespace Assets.Scripts
{
    public class GameClientRxOptions : ScriptableObject
    {
        public int ConnectPort;
        public string ConnectAddress;
        public int BindPort;
        public FloatControllerState.Serializable WalkBlendTree;
        public CinemachineVirtualCamera Camera;
        public int CheckTimeoutFrequencySeconds;
        public GameObject PlayerPrefab;
    }
}