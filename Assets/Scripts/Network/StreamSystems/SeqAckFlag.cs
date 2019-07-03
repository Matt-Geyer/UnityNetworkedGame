using LiteNetLib.Utils;

namespace Assets.Scripts.Network.StreamSystems
{
    /// <summary>
    /// Represents a flag where each bit position represents whether or not a sequence was ACKed
    /// If the bit was set it was ACKed otherwise it was NACKed 
    /// TODO: Add DEBUG conditional checks on the data
    /// </summary>
    public struct SeqAckFlag
    {
        public const int AckFlagSize = 32;
        public const uint LastBitTrueMask = (uint)1 << 31;
        public uint Data;
        public byte StartSeq;
        public byte SeqCount;
        public byte EndSeq;

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(SeqCount);
            if (SeqCount > 0)
            {
                writer.Put(StartSeq);
                writer.Put(Data);
            }
        }

        public void Deserialize(NetDataReader reader)
        {
            SeqCount = reader.GetByte();
            if (SeqCount > 0)
            {
                StartSeq = reader.GetByte();
                Data = reader.GetUInt();
                EndSeq = (byte)(StartSeq + SeqCount - 1);
            }
        }

        public void InitFromData(uint data, byte start, byte count)
        {
            Data = data;
            StartSeq = start;
            SeqCount = count;
            EndSeq = (byte)(start + count - 1);
        }

        public void InitWithAckedSequence(byte seq)
        {
            StartSeq = EndSeq = seq;
            Data = 1;
            SeqCount = 1;
        }

        public bool IsAck(int seqBitPositionInFlag)
        {
            return ((Data >> seqBitPositionInFlag) & 1) == 1;
        }

        public void NackNextSequence()
        {
            if (SeqCount < AckFlagSize)
            {
                Data &= (uint)~(1 << SeqCount);
                SeqCount++;
                EndSeq++;
            }
            else
            {
                Data >>= 1;
                StartSeq++;
                EndSeq++;
            }
        }

        public void AckNextSequence()
        {
            if (SeqCount < AckFlagSize)
            {
                Data |= (uint)1 << SeqCount;
                SeqCount++;
                EndSeq++;
            }
            else
            {
                Data = Data >> 1 | LastBitTrueMask;
                StartSeq++;
                EndSeq++;
            }
        }

        /// <summary>
        /// Seq must be within (seq_start, seq_end) or this will fuck up
        /// </summary>
        /// <param name="seq"></param>
        public void DropStartSequenceUntilItEquals(byte seq)
        {
            while (StartSeq != seq)
            {
                DropStartSequence();
            }
        }

        public void DropStartSequence()
        {
            Data >>= 1;
            StartSeq++;
            SeqCount--;
            // end seq stays the same
        }

        public override string ToString()
        {
            return $"SeqCount: {SeqCount}  StartSeq: {StartSeq}  EndSeq: {EndSeq}  Data: {Data}";
        }
    }
}