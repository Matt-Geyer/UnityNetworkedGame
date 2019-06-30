using LiteNetLib.Utils;

namespace Assets.Scripts
{
    public interface IPacketStreamReader
    {
        void ReadPacketStream(NetDataReader stream);
    }
}