using System.Collections.Generic;

namespace Assets.Scripts
{
    public class PacketTransmissionRecord
    {
        // Packet stream system data
        public byte Seq;
        public SeqAckFlag AckFlag;
    }
}