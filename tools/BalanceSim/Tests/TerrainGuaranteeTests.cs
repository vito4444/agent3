using NUnit.Framework;
using Starsoil.Core;

namespace Starsoil.BalanceSim.Tests
{
    /// <summary>M1-T1: seeded generation with start-region resource guarantees (docs/plan/02).</summary>
    public sealed class TerrainGuaranteeTests
    {
        private const int SeedCount = 100;
        private const int RegionSize = 192;

        private static readonly string[] GuaranteedItems =
        {
            ItemIds.IronOre, ItemIds.CopperOre, ItemIds.QuartzSand,
            ItemIds.SaltOre, ItemIds.Carbon, ItemIds.Ice
        };

        [Test]
        public void HundredSeeds_GuaranteedDepositsWithin60_AndShrubs()
        {
            for (ulong seed = 1; seed <= SeedCount; seed++)
            {
                var world = new World(seed, RegionSize);
                foreach (string itemId in GuaranteedItems)
                {
                    Assert.GreaterOrEqual(
                        world.Nodes.CountWithin(itemId, world.StartX, world.StartY, Balance.GuaranteedDepositRadius),
                        1,
                        "seed " + seed + " missing " + itemId + " within " + Balance.GuaranteedDepositRadius);
                }
                Assert.GreaterOrEqual(
                    world.Nodes.CountWithin(ItemIds.Biomass, world.StartX, world.StartY, Balance.GuaranteedDepositRadius),
                    Balance.MinShrubsNearStart,
                    "seed " + seed + " has too few shrubs near start");
            }
        }

        [Test]
        public void SameSeed_IdenticalTerrainAndNodes()
        {
            var a = new World(7UL, RegionSize);
            var b = new World(7UL, RegionSize);
            for (int y = 0; y < RegionSize; y++)
            {
                for (int x = 0; x < RegionSize; x++)
                {
                    if (a.Terrain.GetHeight(x, y) != b.Terrain.GetHeight(x, y))
                    {
                        Assert.Fail("terrain differs at " + x + "," + y);
                    }
                }
            }
            Assert.AreEqual(a.Nodes.All.Count, b.Nodes.All.Count);
            foreach (var pair in a.Nodes.All)
            {
                Assert.IsTrue(b.Nodes.TryGet(pair.Key, out var other));
                Assert.AreEqual(pair.Value.ItemId, other.ItemId);
                Assert.AreEqual(pair.Value.X, other.X);
                Assert.AreEqual(pair.Value.Y, other.Y);
                Assert.AreEqual(pair.Value.Remaining, other.Remaining);
            }
        }
    }
}
