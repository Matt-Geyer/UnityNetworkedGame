using LiteNetLib.Utils;

namespace Assets.Scripts.Network.StreamSystems
{
    public interface IPacketStreamWriter
    {
        void WriteToPacketStream(NetDataWriter stream);
    }
}