namespace Assets.Scripts.Network.StreamSystems
{
    public class PacketTransmissionRecord
    {
        // Packet stream system data
        public byte Seq;
        public SeqAckFlag AckFlag;
    }
}