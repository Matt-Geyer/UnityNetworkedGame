using System.Collections.Generic;
using LiteNetLib.Utils;

namespace Assets.Scripts
{
    public abstract class ReplicatableObject : IPersistentObject
    {
        public abstract void Serialize(NetDataWriter writer, uint stateMask);

        protected readonly Dictionary<int, ReplicationRecord> ReplicationRecords = new Dictionary<int, ReplicationRecord>();

        public abstract PersistentObjectRep ObjectRep { get; set; }

        public abstract void UpdateStateMask();

        public virtual ReplicationRecord GetReplicationRecord(int replicationSystemId)
        {
            ReplicationRecords.TryGetValue(replicationSystemId, out ReplicationRecord record);
            return record;
        }

        public virtual bool IsReplicatedBySystem(int replicationSystemId)
        {
            return ReplicationRecords.ContainsKey(replicationSystemId);
        }

        public virtual void AddReplicationRecord(ReplicationRecord record)
        {
            ReplicationRecords[record.ReplicationSystemId] = record;
        }

        public virtual void RemoveReplicationRecord(int replicationSystemId)
        {
            ReplicationRecords.Remove(replicationSystemId);
        }

        public abstract void Serialize(NetDataWriter writer);

        public abstract void Deserialize(NetDataReader reader);

    }
}