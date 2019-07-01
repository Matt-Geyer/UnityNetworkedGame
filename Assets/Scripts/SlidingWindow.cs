using System;

namespace Assets.Scripts
{
    public class SlidingWindow<T> where T : SeqBase
    {
        public int Count;
        public int First { get; private set; }
        public readonly T[] Items;
        public int Last { get; private set; }
        public readonly int Max;
        private ushort _seq;
        public readonly ushort UpperSeqWindow;

        public SlidingWindow(int max, Func<T> factory)
        {
            Items = new T[max];
            for (int i = 0; i < max; i++) Items[i] = factory();
            First = Last = 0;
            Count = 0;
            Max = max;
            UpperSeqWindow = (ushort)(ushort.MaxValue - Max);
        }

        public T GetNextAvailable()
        {
            if (Count == Max) return null;
            T item = Items[Last];
            item.Seq = _seq;
            Last = ++Last < Max ? Last : 0;
            Count++;
            _seq++;
            return item;
        }

        public void AckSeq(ushort seq)
        {
            if (Count == 0) return;

            int lastInd = Last - 1 >= 0 ? Last - 1 : Max - 1;

            ushort firstSeq = Items[First].Seq;
            ushort lastSeq = Items[lastInd].Seq;

            // If the seq isn't inside the range of our window then we don't care about it
            if (!SequenceHelper.SeqIsInsideRangeInclusive(firstSeq, lastSeq, seq, Max)) return;

            // drop moves off the front of the window until the window starts at seq + 1 or count = 0
            int targetSeq = seq + 1;
            while (Count > 0 && Items[First].Seq != targetSeq)
            {
                First = ++First < Max ? First : 0;
                Count--;
            }
        }

        public override string ToString()
        {
            string str = $"Count: {Count} First Index: {First} Last Index: {Last} ";
            if (Count <= 0) return str;
            int last = Last - 1 < 0 ? Max - 1 : Last - 1;
            str += $"First Seq: {Items[First].Seq} Last Seq: {Items[last].Seq}";
            return str;
        }
    }
}