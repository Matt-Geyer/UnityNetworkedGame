using LiteNetLib.Utils;

namespace Assets.Scripts
{
    public interface IPacketStreamWriter
    {
        void WriteToPacketStream(NetDataWriter stream);
    }
}