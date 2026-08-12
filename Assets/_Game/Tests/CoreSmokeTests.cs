using NUnit.Framework;
using Starsoil.Core;

namespace Starsoil.Tests
{
    /// <summary>
    /// Unity-side smoke tests proving the EditMode test infrastructure works against
    /// Game.Core (CI gate 2). The full sim test suite lives in tools/BalanceSim and
    /// runs in CI gate 1 without Unity (docs/plan/08 test strategy).
    /// </summary>
    public sealed class CoreSmokeTests
    {
        private const ulong Seed = 7UL;
        private const int RegionSize = 96;

        [Test]
        public void World_PlacesBuildingViaCommand_AndTicks()
        {
            var world = new World(Seed, RegionSize);
            int center = RegionSize / 2;

            world.Commands.Enqueue(new PlaceBuildingCommand
            {
                DefId = BuildingDefs.TestBlockId,
                X = center,
                Y = center,
                Rotation = 0
            });
            world.Step();

            Assert.AreEqual(1, world.Buildings.Count);

            const int extraTicks = 100;
            for (int i = 0; i < extraTicks; i++)
            {
                world.Step();
            }
            Assert.AreEqual(extraTicks + 1, world.Tick);
        }

        [Test]
        public void World_SameSeed_SameHash()
        {
            var a = new World(Seed, RegionSize);
            var b = new World(Seed, RegionSize);
            Assert.AreEqual(a.ComputeStateHash(), b.ComputeStateHash());
        }
    }
}
