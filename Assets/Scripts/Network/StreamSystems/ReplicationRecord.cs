using System;

namespace Assets.Scripts.Network.StreamSystems
{
    public class ReplicationRecord
    {
        public uint StateMask;

        [Flags]
        public enum ReplicationSystemStatus
        {
            None = 0,
            Added = 1,
            Removed = 2
        }

        public ReplicationSystemStatus Status = ReplicationSystemStatus.None;
        public ushort Id;
        public int ReplicationSystemId;
        public float Priority;
        public ReplicatableObject Entity; // may even change this actually to an ID in some game array to make it closer to ECS for when i eventually switch??
        public ReplicatedObjectTransmissionRecord LastTransmission;
    }
}