using LiteNetLib.Utils;

namespace Assets.Scripts.Network.StreamSystems
{
    public interface IPacketStreamReader
    {
        void ReadPacketStream(NetDataReader stream);
    }
}