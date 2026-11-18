using NUnit.Framework;

namespace Game.Domain.Tests
{
    public class SeededRandomTests
    {
        [Test]
        public void SameSeed_ProducesSameSequence()
        {
            var a = new SeededRandom(42);
            var b = new SeededRandom(42);
            for (int i = 0; i < 1000; i++)
            {
                Assert.AreEqual(a.NextULong(), b.NextULong());
            }
        }

        [Test]
        public void KnownFirstValue_IsStableAcrossPlatforms()
        {
            // SplitMix64 reference output for seed 0. Guards against accidental algorithm changes,
            // which would silently change every saved generator sequence.
            var rng = new SeededRandom(0);
            Assert.AreEqual(0xE220A8397B1DCDAFUL, rng.NextULong());
        }

        [Test]
        public void RestoringState_ResumesSequence()
        {
            var a = new SeededRandom(7);
            a.NextULong();
            ulong saved = a.State;
            ulong expected = a.NextULong();
            var b = new SeededRandom(1) { State = saved };
            Assert.AreEqual(expected, b.NextULong());
        }

        [Test]
        public void NextInt_StaysInRange_AndCoversAllValues()
        {
            var rng = new SeededRandom(99);
            var counts = new int[7];
            for (int i = 0; i < 70000; i++)
            {
                int v = rng.NextInt(7);
                Assert.That(v, Is.InRange(0, 6));
                counts[v]++;
            }

            foreach (int c in counts)
            {
                Assert.That(c, Is.InRange(9000, 11000), "distribution looks biased");
            }
        }

        [Test]
        public void NextInt_RejectsNonPositiveBound()
        {
            var rng = new SeededRandom(1);
            Assert.Throws<System.ArgumentOutOfRangeException>(() => rng.NextInt(0));
        }
    }
}
