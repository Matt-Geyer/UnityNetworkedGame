namespace Assets.Scripts.Network.StreamSystems
{
    public class ReplicatedObjectTransmissionRecord
    {
        public ReplicationRecord.ReplicationSystemStatus Status;

        public uint StateMask;

        public ReplicationRecord RepRecord;

        public ReplicatedObjectTransmissionRecord NextTransmission;
    }
}