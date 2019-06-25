using LiteNetLib.Utils;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ReplicatableGameObject : ReplicatableObject
{
    [Flags]
    public enum StateFlag
    {
        None = 0,
        Pos = 1,
        Rotation = 2
    }

    public bool hasPos;
    public int pos;
    public bool hasRotation;
    public float rotation;

    public static PersistentObjectRep ObjectRep;

    public override void Serialize(NetDataWriter writer, Enum mask)
    {

        StateFlag Mask = (StateFlag)mask;

        if ((Mask & StateFlag.Pos) == StateFlag.Pos)
        {
            hasPos = true;
            Console.WriteLine($"WRITING POS {pos}");
        }
        else
        {
            hasPos = false;
        }
        if ((Mask & StateFlag.Rotation) == StateFlag.Rotation)
        {
            hasRotation = true;
            Console.WriteLine($"WRITING ROTATION {rotation}");
        }
        else
        {
            hasRotation = false;
        }
    }

    public void Deserialize()
    {
        throw new NotImplementedException();
    }

    public void SetStaticObjectRep(PersistentObjectRep rep)
    {
        ObjectRep = rep;
    }
}

public abstract class ReplicatableObject
{
    public abstract void Serialize(NetDataWriter writer, Enum stateMask);

    protected readonly Dictionary<int, ReplicationRecord> ReplicationRecords = new Dictionary<int, ReplicationRecord>();

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
}

public class ReplicationRecord
{
    public Enum StateMask;

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
}

public class ReplicationSystem
{
    public int Id;

    public ushort NextId = 0;

    private readonly Dictionary<ushort, ReplicationRecord> ReplicatedObjects = new Dictionary<ushort, ReplicationRecord>();

    public void StartReplicating(ReplicatableObject obj)
    {
        ReplicationRecord record = new ReplicationRecord
        {
            Id = NextId++,
            ReplicationSystemId = Id,
            Status = ReplicationRecord.ReplicationSystemStatus.Added
        };

        obj.AddReplicationRecord(record);

        ReplicatedObjects[record.Id] = record;
    }

    public void StopReplicating(ReplicatableObject obj)
    {
        // Will actually be removed once acked by remote rep manager?
        obj.GetReplicationRecord(Id).Status = ReplicationRecord.ReplicationSystemStatus.Removed;
    }

    public void ProcessNotification(PacketTransmissionRecord notification)
    {

    }

    public void UpdateIncoming(NetDataReader stream)
    {

    }

    public void UpdateOutgoing(NetDataWriter stream)
    {

    }
}
