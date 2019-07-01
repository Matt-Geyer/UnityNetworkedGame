using Assets.Scripts;
using NUnit.Framework;

namespace Assets.Tests
{
    public class TestSlidingWindow
    {
        private class SeqTest : SeqBase
        {

        }

        [Test]
        public void TestMaxItems()
        {
            SlidingWindow<SeqTest> window = new SlidingWindow<SeqTest>(1, () => new SeqTest());

            Assert.AreEqual(0, window.Count);

            SeqTest seqItem = window.GetNextAvailable();

            Assert.AreEqual(1, window.Count);

            Assert.IsNotNull(seqItem);
            Assert.AreEqual(0, seqItem.Seq);

            // Window is full so should be null
            seqItem = window.GetNextAvailable();

            Assert.IsNull(seqItem);

        }

        [Test]
        public void TestReserveNextEmptySample()
        {
            SlidingWindow<SeqTest> window = new SlidingWindow<SeqTest>(2, () => new SeqTest());
            SeqTest seqItem = window.GetNextAvailable();
            Assert.IsNotNull(seqItem);
            Assert.AreEqual(1, window.Count);
            Assert.AreEqual(0, seqItem.Seq);
            seqItem = window.GetNextAvailable();
            Assert.IsNotNull(seqItem);
            Assert.AreEqual(2, window.Count);
            Assert.AreEqual(1, seqItem.Seq);
        }

        [Test]
        public void TestAckSeqNormal()
        {
            SlidingWindow<SeqTest> window = new SlidingWindow<SeqTest>(10, () => new SeqTest());


            SeqTest seqItem = window.GetNextAvailable();

            Assert.AreEqual(1, window.Count);
            Assert.IsNotNull(seqItem);
            Assert.AreEqual(0, seqItem.Seq);

            window.AckSeq(seqItem.Seq);
            Assert.AreEqual(0, window.Count);

            seqItem = window.GetNextAvailable();

            Assert.IsNotNull(seqItem);
            Assert.AreEqual(1, seqItem.Seq);

            window.AckSeq(seqItem.Seq);

            Assert.AreEqual(0, window.Count);

            window.GetNextAvailable();
            window.GetNextAvailable();
            window.GetNextAvailable();
            seqItem = window.GetNextAvailable();


            Assert.AreEqual(4, window.Count);
            Assert.AreEqual(5, seqItem.Seq);

            window.AckSeq(seqItem.Seq);

            Assert.AreEqual(0, window.Count);
        }
    }
}
