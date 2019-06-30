namespace Assets.Scripts
{
    public class ReplicatedObjectTransmissionRecord
    {
        public ReplicationRecord.ReplicationSystemStatus Status;

        public uint StateMask;

        public ReplicationRecord RepRecord;

        public ReplicatedObjectTransmissionRecord NextTransmission;
    }
}