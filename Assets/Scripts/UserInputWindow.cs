using System;

namespace Assets.Scripts
{
    public abstract class SeqBase
    {
        public virtual ushort Seq { get; set; }
    }

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
            // if seq > and inside window 
            // 223 is byte.MaxValue - 32
            ushort firstSeq = Items[First].Seq;

            if (firstSeq != seq && (seq <= firstSeq || seq - firstSeq > Max) &&
                (seq >= firstSeq || firstSeq <= UpperSeqWindow ||
                 seq >= (ushort)(Max - (ushort.MaxValue - firstSeq)))) return;

            // drop moves off the front of the window until the window starts at seq + 1 or count = 0
            int targetSeq = seq + 1;
            while (Count > 0 && Items[First].Seq != targetSeq)
            {
                First = ++First < Max ? First : 0;
                Count--;
            }
        }
    }
}