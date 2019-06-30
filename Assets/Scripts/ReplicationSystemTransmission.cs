using System.Collections.Generic;

namespace Assets.Scripts
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