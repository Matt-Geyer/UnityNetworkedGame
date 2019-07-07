using System;
using System.Collections.Generic;
using System.Linq;

namespace Assets.Scripts.Network.StreamSystems
{

    public class SlidingList<T> where T : SeqBase
    {
        public readonly List<T> Items;

        public readonly int MaxItems;

        private readonly Func<T> _factory;

        private ushort _seq;

        public SlidingList(int count, Func<T> factory)
        {
            MaxItems = count;
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            Items = new List<T>(count);
        }

        public T GetNextAvailable()
        {
            if (Items.Count == MaxItems)
            {
                return null;
            }

            T item = _factory();

            item.Seq = _seq++;

            Items.Add(item);

            return item;
        }

        public void AckSequence(ushort sequence)
        {
            if (Items.Count == 0) return;

            T first = Items[0];
            T last = Items.Last();

            while (first != null && SequenceHelper.SeqIsInsideRangeInclusive(first.Seq, last.Seq, sequence, MaxItems) && Items.Count > 0)
            {
                Items.RemoveAt(0);
                first = Items.FirstOrDefault();
            }
        }

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