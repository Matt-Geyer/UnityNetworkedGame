using System.Collections.Generic;

namespace Assets.Scripts.Network.StreamSystems
{
    public class ReplicationSystemTransmission
    {
        public List<ReplicatedObjectTransmissionRecord> Records;

        public ReplicationSystemTransmission()
        {
            Records = new List<ReplicatedObjectTransmissionRecord>();
        }
    }
}