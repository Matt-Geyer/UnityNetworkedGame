using System.Collections.Generic;
using LiteNetLib.Utils;

namespace Assets.Scripts
{
    public interface IReplicationSystem : IPacketStreamReader, IPacketStreamWriter, IPacketTransmissionNotificationReceiver
    {
        void StartReplicating(ReplicatableObject obj);
        void StopReplicating(ReplicatableObject obj);
    }
}