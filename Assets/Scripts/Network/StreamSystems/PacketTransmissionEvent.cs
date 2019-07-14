using LiteNetLib.Utils;

namespace Assets.Scripts.Network.StreamSystems
{
    public sealed class PacketTransmissionEvent
    {
        public readonly PacketTxRecord TxRecord;

        public readonly NetDataWriter StreamWriter;
    }
}