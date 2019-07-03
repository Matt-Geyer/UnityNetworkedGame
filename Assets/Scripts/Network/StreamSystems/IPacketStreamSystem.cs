using LiteNetLib;

namespace Assets.Scripts.Network.StreamSystems
{
    public interface IPacketStreamSystem
    {
        void UpdateIncoming(bool host = false);
        void AddDataReceivedEvents(params NetEvent[] events);
        void AddDataReceivedEvent(NetEvent evt);
        void UpdateOutgoing(bool host = false);
    }
}