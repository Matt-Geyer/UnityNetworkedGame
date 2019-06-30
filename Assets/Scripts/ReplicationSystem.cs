using System;
using System.Collections.Generic;
using AiUnity.NLog.Core;
using LiteNetLib.Utils;

namespace Assets.Scripts
{
    public class ReplicationSystem : IReplicationSystem
    {
        private readonly Queue<ReplicationSystemTransmission> _transmissions;

        public readonly Dictionary<ushort, ReplicationRecord> ReplicatedObjects =
            new Dictionary<ushort, ReplicationRecord>();

        public int Id;
        public NLogger Log;
        public ushort NextId = 1;

        public ReplicationSystem()
        {
            Log = NLogManager.Instance.GetLogger(this);
            _transmissions = new Queue<ReplicationSystemTransmission>();
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
            // Will actually be removed once ACKed by remote rep manager?
            obj.GetReplicationRecord(Id).Status = ReplicationRecord.ReplicationSystemStatus.Removed;
        }

        public void ReceiveNotifications(List<bool> notifications)
        {
            foreach (bool dropped in notifications)
            {
                ReplicationSystemTransmission transmission = _transmissions.Dequeue();

                if (!dropped) continue;

                foreach (ReplicatedObjectTransmissionRecord tr in transmission.Records)
                {
                    // Since this packet was lost we know that the client hasn't synced the state
                    // represented in the state mask for this object so we want to set those bits
                    // in the objects state mask again so that those pieces of state are transmitted this iteration
                    ReplicatedObjectTransmissionRecord nextTransmission = tr.NextTransmission;
                    while (nextTransmission != null)
                    {
                        // We want to exclude any bits that were set in transmissions that came after the one we are being notified about
                        tr.StateMask ^= tr.StateMask & nextTransmission.StateMask;
                        tr.Status ^= tr.Status & nextTransmission.Status;
                        nextTransmission = nextTransmission.NextTransmission;
                    }

                    // The state mask in tr.StateMask now only contains bits that weren't
                    // set set in subsequent transmissions so we want to make sure all those bits
                    // are set for the next transmission
                    tr.RepRecord.StateMask |= tr.StateMask;
                    tr.RepRecord.Status |= tr.Status;
                }
            }
        }

        public void ReadPacketStream(NetDataReader stream)
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
                            Log.Debug($"Removing record: {record.Id}");
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

        public void WriteToPacketStream(NetDataWriter stream)
        {
            ReplicationSystemTransmission transmission = new ReplicationSystemTransmission();

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
                ReplicatedObjectTransmissionRecord tr = new ReplicatedObjectTransmissionRecord
                {
                    StateMask = r.StateMask,
                    Status = r.Status,
                    RepRecord = r // not loving this
                };

                // This is the easiest way I could think of to reference the latest transmission 
                if (r.LastTransmission != null) r.LastTransmission.NextTransmission = tr;
                r.LastTransmission = tr;

                transmission.Records.Add(tr);

                // Clear masks
                r.Status = ReplicationRecord.ReplicationSystemStatus.None;
                r.StateMask = 0;
            }

            // Write 0 which isn't a valid id so the remote stream will know that's the end of the data
            stream.Put((ushort)0);

            _transmissions.Enqueue(transmission);
        }
    }
}