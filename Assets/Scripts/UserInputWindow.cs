namespace Assets.Scripts
{
    public class UserInputWindow
    {
        public int Count;
        public int First;
        public UserInputSample[] Input;
        public int Last;
        public int Max;
        public IUserInputUtils Sampler;
        public ushort Seq;
        public ushort UpperSeqWindow;

        public void Init(int max)
        {
            Input = new UserInputSample[max];
            for (int i = 0; i < max; i++) Input[i] = new UserInputSample();
            First = Last = 0;
            Count = 0;
            Max = max;
            UpperSeqWindow = (ushort)(ushort.MaxValue - Max);
        }

        public int SampleUserInput()
        {
            if (Count == Max) return -1;
            int sampleIndex = Last;
            Sampler.Sample(Input[sampleIndex]);
            Input[sampleIndex].Seq = Seq;
            Last = ++Last < Max ? Last : 0;
            Count++;
            Seq++;
            return sampleIndex;
        }

        public void AckSeq(ushort seq)
        {
            // if seq > and inside window 
            // 223 is byte.MaxValue - 32
            ushort firstSeq = Input[First].Seq;

            if (firstSeq != seq && (seq <= firstSeq || seq - firstSeq > Max) &&
                (seq >= firstSeq || firstSeq <= UpperSeqWindow ||
                 seq >= (ushort)(Max - (ushort.MaxValue - firstSeq)))) return;

            // drop moves off the front of the window until the window starts at seq + 1 or count = 0
            int targetSeq = seq + 1;
            while (Count > 0 && Input[First].Seq != targetSeq)
            {
                First = ++First < Max ? First : 0;
                Count--;
            }
        }
    }
}