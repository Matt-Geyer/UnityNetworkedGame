namespace Assets.Scripts.Network.StreamSystems
{
    public class SequenceHelper
    {
        public static bool SeqIsAheadButInsideWindow32(byte current, byte check)
        {
            // 223 is byte.MaxValue - 32
            return ((check > current && (check - current <= 32)) ||
                    (check < current && (current > 223 && check < (byte)(32 - (byte.MaxValue - current)))));
        }

        public static bool SeqIsEqualOrAheadButInsideWindow32(byte current, byte check)
        {
            // 223 is byte.MaxValue - 32
            return (current == check ||
                    (check > current && (check - current <= 32)) ||
                    (check < current && (current > 223 && check < (byte)(32 - (byte.MaxValue - current)))));
        }

        public static bool SeqIsInsideRangeInclusive(byte start, byte end, byte check)
        {
            return SeqIsEqualOrAheadButInsideWindow32(check, end) && SeqIsEqualOrAheadButInsideWindow32(start, check);
        }

        public static bool SeqIsInsideRange(byte start, byte end, byte check)
        {
            // if the end of the range is ahead of the check value, and the check value is ahead of the start of the range
            // then the check value must be inside of the range
            return SeqIsAheadButInsideWindow32(check, end) && SeqIsAheadButInsideWindow32(start, check);
        }

        public static bool SeqIsEqualOrAheadButInsideWindow(ushort current, ushort check, int window)
        {
            return (current == check ||
                    (check > current && (check - current <= window)) ||
                    (check < current && (current > ushort.MaxValue - window && check < (ushort)(window - (ushort.MaxValue - current)))));
        }

        public static bool SeqIsInsideRangeInclusive(ushort start, ushort end, ushort check, int window)
        {
            return SeqIsEqualOrAheadButInsideWindow(check, end, window) && SeqIsEqualOrAheadButInsideWindow(start, check, window);
        }
    }
}