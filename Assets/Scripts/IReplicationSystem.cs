using System.Collections.Generic;
using LiteNetLib.Utils;

namespace Assets.Scripts
{
    public interface IReplicationSystem
    {
        void StartReplicating(ReplicatableObject obj);
        void StopReplicating(ReplicatableObject obj);
        void ProcessNotifications(List<PacketTransmissionRecord> notifications);
        void ProcessReplicationData(NetDataReader stream);
        void WriteReplicationData(NetDataWriter stream, PacketTransmissionRecord packetTransmissionRecord);
    }
}