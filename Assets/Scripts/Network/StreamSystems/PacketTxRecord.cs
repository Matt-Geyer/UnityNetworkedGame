using System.Collections.Generic;

namespace Assets.Scripts.Network.StreamSystems
{
    public sealed class PacketTxRecord
    {
        public bool Dropped;

        public Dictionary<string, object> TransmissionData;
    }
}