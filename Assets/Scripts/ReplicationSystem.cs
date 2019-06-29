using AiUnity.NLog.Core;
using LiteNetLib.Utils;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

public class ReplicatableGameObject : ReplicatableObject
{
    [Flags]
    public enum StateFlag
    {
        None = 0,
        Position = 1,
        Rotation = 2
    }

    public static readonly StateFlag AllStates = 
        StateFlag.Position |
        StateFlag.Rotation; 

    public bool hasPos;
    public Vector3 Position;
    public bool hasRotation;
    public float rotation;

    private ReplicatableGameObject LastFrame; 

    public StateFlag ChangedStates;

    public static PersistentObjectRep StaticObjectRep;

    public override PersistentObjectRep ObjectRep
    {
        get => StaticObjectRep;
        set => StaticObjectRep = value;
    }

    public override void Serialize(NetDataWriter writer, uint mask)
    {
        StateFlag Mask = (StateFlag)mask;

        Debug.Log($"mask: {mask}  Mask: {Mask}");

        if ((Mask & StateFlag.Position) == StateFlag.Position)
        {
            writer.Put((byte)1);
            writer.Put(Position.x);
            writer.Put(Position.y);
            writer.Put(Position.z);
            Debug.Log($"Wrote positions {Position.ToString()}");
        }
        else
        {
            Debug.Log($"No position to write");
            writer.Put((byte)0);
        }
    }

    public override void Serialize(NetDataWriter writer)
    {
        Serialize(writer, (uint)AllStates);
    }

    public override void Deserialize(NetDataReader reader)
    {
        // when deserialized that means this object is being replicated
        // so i think the interpolation logic has to exist somewhat inside this obj
        // at the very least prev frame data?

        if (reader.GetByte() == 1)
        {
            Position.x = reader.GetFloat();
            Position.y = reader.GetFloat();
            Position.z = reader.GetFloat();
        }
    }

    public override void UpdateStateMask()
    {
        if (LastFrame == null)
        {
            ChangedStates = AllStates;
            LastFrame = new ReplicatableGameObject();
        }
        else
        {    
            // not sure exactly how this will look just yet
            ChangedStates = StateFlag.None;
            if (LastFrame.Position != Position)
            {
                Debug.Log($"{Time.frameCount} : Position changed!");            
                ChangedStates |= StateFlag.Position;
            }
        }

        LastFrame.CopyStateFrom(this);

        uint changedMask = Convert.ToUInt32(ChangedStates);

        foreach (ReplicationRecord r in ReplicationRecords.Values)
        {
            r.StateMask = r.StateMask | changedMask;
        }
    }

    public void CopyStateFrom(ReplicatableGameObject original)
    {
        Position = original.Position;
    }
}

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

public class ReplicatedObjectTransmissionRecord
{
    public ReplicationRecord.ReplicationSystemStatus Status;

    public uint StateMask;

    public ReplicationRecord RepRecord;

    public ReplicatedObjectTransmissionRecord NextTransmission;
}


public class ReplicationRecord
{
    public uint StateMask;

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

public class ReplicationSystem
{
    public int Id;
    public ushort NextId = 1;
    public readonly Dictionary<ushort, ReplicationRecord> ReplicatedObjects = new Dictionary<ushort, ReplicationRecord>();
    public NLogger Log;

    public ReplicationSystem()
    {
        Log = NLogManager.Instance.GetLogger(this);
    }

    public void StartReplicating(ReplicatableObject obj)
    {
        ReplicationRecord record = new ReplicationRecord
        {
            Id = NextId++,
            ReplicationSystemId = Id,
            Status = ReplicationRecord.ReplicationSystemStatus.Added,
            Entity = obj
        };

        obj.AddReplicationRecord(record);

        ReplicatedObjects[record.Id] = record;
    }

    public void StopReplicating(ReplicatableObject obj)
    {
        // Will actually be removed once acked by remote rep manager?
        obj.GetReplicationRecord(Id).Status = ReplicationRecord.ReplicationSystemStatus.Removed;
    }

    public void ProcessNotifications(List<PacketTransmissionRecord> notifications)
    {
        foreach (PacketTransmissionRecord notification in notifications)
        {
            if (notification.Received == false)
            {
                foreach (ReplicatedObjectTransmissionRecord rotr in notification.ReplicationTransmissions)
                {
                    // Since this packet was lost we know that the client hasn't synced the state
                    // represented in the state mask for this object so we want to set those bits
                    // in the objects state mask again so that those pieces of state are transmitted this iteration
                    ReplicatedObjectTransmissionRecord nextRotr = rotr.NextTransmission;
                    while (nextRotr != null)
                    {
                        // We want to exclude any bits that were set in transmissions that came after the one we are being notified about
                        rotr.StateMask = rotr.StateMask ^ (rotr.StateMask & nextRotr.StateMask);
                        rotr.Status = rotr.Status ^ (rotr.Status & nextRotr.Status);
                        nextRotr = nextRotr.NextTransmission;
                    }

                    // The state mask in rotr.StateMask now only contains bits that weren't
                    // set set in subsequent transmissions so we want to make sure all those bits
                    // are set for the next transmission
                    rotr.RepRecord.StateMask |= rotr.StateMask;
                    rotr.RepRecord.Status |= rotr.Status;
                }
            }
            else
            {
                // ?
            }
        }
    }

    public void ProcessReplicationData(NetDataReader stream)
    {
        try
        {
            ushort repObjId = stream.GetUShort();
            while (repObjId != 0)
            {
                Log.Debug($"Reading ghost with id: {repObjId}");
                // first read if there is a status change
                if (stream.GetByte() == 1)
                {
         
                    // read the status change
                    if (stream.GetByte() == 1)
                    {
                        Log.Debug("status changed: ADDED");
                        // added so read the persistent obj id 
                        byte objRepId = stream.GetByte();

                        // Create new instance of the object
                        IPersistentObject obj = PersistentObjectManager.CreatePersistentObject(objRepId);

                        // unpack stream data
                        obj.Deserialize(stream);

                        ReplicatedObjects[repObjId] = new ReplicationRecord
                        {
                            Id = repObjId,
                            Entity = obj as ReplicatableObject
                        };
                    }
                    else
                    {
                        Log.Debug("status changed: REMOVED");
                        // remove the record but also need to destroy game object or queue it to be destroyed..
                        if (ReplicatedObjects.TryGetValue(repObjId, out ReplicationRecord record))
                        {
                            //GameObject.Destroy(record.Entity);
                            ReplicatedObjects.Remove(repObjId);
                        }
                    }
                }
                else
                {
                    Log.Debug("State update");
                    // no status change just new state information  so unpack into existing replicated obj
                    ReplicatedObjects[repObjId].Entity.Deserialize(stream);
                }
                repObjId = stream.GetUShort();
            }
        }
        catch (Exception e)
        {
            Log.Debug(e.Message);
            throw e;
        }     
    }

    public void WriteReplicationData(NetDataWriter stream, PacketTransmissionRecord packetTransmissionRecord)
    {

        try
        {
            // sort by state change and then priority once it exists 
            // TODO: flow control
            // how know if we overflow the buffer before hand or keep an index
            // for each write that doesn't overflow and then clear up to that index
            // as soon as we go over. Can keep the max that rep system is allowed to write still within 
            // the actual buffer size 
            foreach (ReplicationRecord r in ReplicatedObjects.Values)
            {
                if (r.StateMask == 0 && r.Status == ReplicationRecord.ReplicationSystemStatus.None) continue;

                Log.Debug($"Writing ghost: {r.Id}");
                // Write the Id of the object that is referenced by the remote ReplicationSystem
                stream.Put(r.Id);
                // Write the state of the replicated object (need bitpacker so that this takes at most 2 bits)
                if (r.Status == ReplicationRecord.ReplicationSystemStatus.None)
                {
                    Log.Debug("No status change");
                    stream.Put((byte)0);
                }
                else
                {
                    stream.Put((byte)1);
                    if (r.Status == ReplicationRecord.ReplicationSystemStatus.Added)
                    {
                       
                        stream.Put((byte)1);
                        // Write persistent object id for obj
                        stream.Put(r.Entity.ObjectRep.Id);
                        Log.Debug($"Status: ADDED. Writing object rep id: {r.Entity.ObjectRep.Id}");
                    }
                    else
                    {
                        Log.Debug("Status: REMOVED");
                        // removed
                        stream.Put((byte)0);
                    }
                }

                Log.Debug($"Serializing object into stream. Bytes before: {stream.Length}");
                // Write the object into the stream using the state mask for this rep system
                r.Entity.Serialize(stream, r.StateMask);
                Log.Debug($"After serializing size: {stream.Length}");


                // Write state and status to transmission record
                ReplicatedObjectTransmissionRecord transmission = new ReplicatedObjectTransmissionRecord
                {
                    StateMask = r.StateMask,
                    Status = r.Status,
                    RepRecord = r // not loving this
                };

                // This is the easiest way I could think of to reference the latest transmission 
                if (r.LastTransmission != null)
                {
                    r.LastTransmission.NextTransmission = transmission;
                }
                r.LastTransmission = transmission;

                packetTransmissionRecord.ReplicationTransmissions.Add(transmission);
                
                // Clear masks
                r.Status = ReplicationRecord.ReplicationSystemStatus.None;
                r.StateMask = 0;

            }
            // Write 0 which isn't a valid id so the remote stream will know thats the end of the data
            stream.Put((ushort)0);

        }
        catch (Exception e)
        {
            Log.Debug(e.Message);
            throw e;
        }       
    }
}
