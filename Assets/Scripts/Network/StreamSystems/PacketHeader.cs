using LiteNetLib.Utils;

namespace Assets.Scripts.Network.StreamSystems
{
    public struct PacketHeader
    {
        public byte Seq;

        public SeqAckFlag AckFlag;

        public byte DataFlag;

        public void Deserialize(NetDataReader reader)
        {
            Seq = reader.GetByte();
            AckFlag.Deserialize(reader);
        }

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(Seq);
            AckFlag.Serialize(writer);
        }
    }
}