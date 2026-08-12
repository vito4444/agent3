using NUnit.Framework;
using Starsoil.Core;

namespace Starsoil.BalanceSim.Tests
{
    public sealed class RngTests
    {
        private const ulong Seed = 12345UL;
        private const int SampleCount = 1000;

        [Test]
        public void SameStream_Reproducible()
        {
            var a = Rng.CreateStream(Seed, "terrain");
            var b = Rng.CreateStream(Seed, "terrain");
            for (int i = 0; i < SampleCount; i++)
            {
                Assert.AreEqual(a.NextUInt(), b.NextUInt());
            }
        }

        [Test]
        public void DifferentStreamNames_Diverge()
        {
            var a = Rng.CreateStream(Seed, "terrain");
            var b = Rng.CreateStream(Seed, "hazards");
            bool anyDifferent = false;
            for (int i = 0; i < SampleCount; i++)
            {
                if (a.NextUInt() != b.NextUInt())
                {
                    anyDifferent = true;
                    break;
                }
            }
            Assert.IsTrue(anyDifferent, "streams with different names produced identical sequences");
        }

        [Test]
        public void NextInt_StaysInRange()
        {
            var rng = Rng.CreateStream(Seed, "range");
            const int min = -3;
            const int max = 11;
            for (int i = 0; i < SampleCount; i++)
            {
                int v = rng.NextInt(min, max);
                Assert.GreaterOrEqual(v, min);
                Assert.Less(v, max);
            }
        }

        [Test]
        public void NextFloat_UnitInterval()
        {
            var rng = Rng.CreateStream(Seed, "floats");
            for (int i = 0; i < SampleCount; i++)
            {
                float v = rng.NextFloat();
                Assert.GreaterOrEqual(v, 0f);
                Assert.Less(v, 1f);
            }
        }
    }
}
