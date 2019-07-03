namespace Assets.Scripts.Network.StreamSystems
{
    public interface IReplicationSystem : IPacketStreamReader, IPacketStreamWriter, IPacketTransmissionNotificationReceiver
    {
        void StartReplicating(ReplicatableObject obj);
        void StopReplicating(ReplicatableObject obj);
    }
}