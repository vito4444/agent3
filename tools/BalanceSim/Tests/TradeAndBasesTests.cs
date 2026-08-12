using System.Collections.Generic;
using NUnit.Framework;
using Starsoil.Core;

namespace Starsoil.BalanceSim.Tests
{
    /// <summary>M6-T3/T4/T6/T7: merchant quote board and settlement, bonded sales,
    /// lazy faction bases with stable hashes and ruin persistence.</summary>
    public sealed class TradeAndBasesTests
    {
        private static Universe NewTradeUniverse(ulong seed)
        {
            var universe = TestUtil.NewUniverse(seed, 96);
            universe.FactionLayerVisible = true; // comms array built (M5-T8 precondition).
            return universe;
        }

        [Test]
        public void QuoteBoard_Refreshes_WithMycoSpores_AndSettlesIn6Hours()
        {
            var universe = NewTradeUniverse(161UL);
            universe.PlayerCredits = 500;

            bool refreshed = false;
            for (int i = 0; i < GameConstants.TicksPerHour * 2 && !refreshed; i++)
            {
                universe.Step();
                foreach (var evt in universe.ActiveWorld.Events)
                {
                    if (evt is QuoteBoardRefreshedEvent)
                    {
                        refreshed = true;
                    }
                }
            }
            Assert.IsTrue(refreshed, "quote board must appear once comms are up (M6-T6)");
            Assert.GreaterOrEqual(universe.FactionsSandbox.Quotes.Count, 6, "3 sell + 3 buy");
            bool mycoOnSale = false;
            MerchantQuote buyQuote = null;
            foreach (var quote in universe.FactionsSandbox.Quotes)
            {
                if (quote.MerchantSells && quote.ItemId == "myco_gold_spore")
                {
                    mycoOnSale = true;
                }
                if (quote.MerchantSells && buyQuote == null)
                {
                    buyQuote = quote;
                }
            }
            Assert.IsTrue(mycoOnSale, "菌金胞 must always be on the sell side (M6-T6)");

            // Accept a merchant-sells quote: credits down now, goods land 6h later.
            double creditsBefore = universe.PlayerCredits;
            int stockBefore = universe.ActiveWorld.CountItemEverywhere(buyQuote.ItemId);
            Assert.IsTrue(universe.FactionsSandbox.AcceptQuote(universe, buyQuote.Id));
            Assert.Less(universe.PlayerCredits, creditsBefore, "payment escrows immediately");

            bool settled = false;
            for (int i = 0; i < GameConstants.TicksPerHour * 7 && !settled; i++)
            {
                universe.Step();
                foreach (var evt in universe.ActiveWorld.Events)
                {
                    if (evt is DealSettledEvent)
                    {
                        settled = true;
                    }
                }
            }
            Assert.IsTrue(settled, "deal must settle after 6 game hours (M6-T6)");
            Assert.AreEqual(stockBefore + buyQuote.Count,
                universe.ActiveWorld.CountItemEverywhere(buyQuote.ItemId), "goods must arrive");
        }

        [Test]
        public void Embargo_ClearsTheBoard()
        {
            var universe = NewTradeUniverse(162UL);
            TestUtil.Run(universe.ActiveWorld, 1); // ensure world exists
            for (int i = 0; i < GameConstants.TicksPerHour * 2; i++)
            {
                universe.Step();
            }
            Assert.Greater(universe.FactionsSandbox.Quotes.Count, 0);
            universe.FactionsSandbox.OnPlayerAttackedFaction(FactionSystem.MerchantId);
            long expiry = universe.FactionsSandbox.QuoteBoardExpiresHour;
            for (int i = 0; i < GameConstants.TicksPerHour * 13; i++)
            {
                universe.Step();
            }
            Assert.AreEqual(expiry, universe.FactionsSandbox.QuoteBoardExpiresHour,
                "embargoed merchant must not refresh the board (M6-T6 禁运)");
        }

        [Test]
        public void BondedSale_Beats_QuoteBoard_ByTenPercent()
        {
            var universe = NewTradeUniverse(163UL);
            var cargo = new List<Ingredient> { new Ingredient { ItemId = "steel_plate", Count = 10 } };
            double bonded = universe.FactionsSandbox.BondedSaleValue(universe, cargo);
            double quoteBoard = universe.PriceOf("steel_plate") * 0.9 * 10;
            Assert.AreEqual(quoteBoard * 1.1, bonded, 0.05, "保税区价格 = 报价单收购价 × 1.1 (M6-T7)");

            double before = universe.PlayerCredits;
            universe.QueueCargoTransit(0, "warmmarsh", cargo);
            for (int i = 0; i < GameConstants.TicksPerHour * 20; i++)
            {
                universe.Step();
            }
            Assert.AreEqual(before + bonded, universe.PlayerCredits, 0.05,
                "bonded cargo must settle credits on arrival (账本断言)");
        }

        [Test]
        public void FactionBases_NineTemplates_StableHash_DistinctSignatures()
        {
            var universe = TestUtil.NewUniverse(164UL, 96);
            var signatures = new HashSet<string>();
            foreach (var faction in universe.FactionsSandbox.Factions)
            {
                universe.Bodies.TryGet(faction.HeldBodies[0], out var body);
                for (int tier = 1; tier <= 3; tier++)
                {
                    faction.T = tier;
                    var world = FactionBases.Instantiate(universe.Seed, faction, body, 96);
                    var again = FactionBases.Instantiate(universe.Seed, faction, body, 96);
                    Assert.AreEqual(world.ComputeStateHash(), again.ComputeStateHash(),
                        "hash(种子, factionId, bodyId) 驱动的实例化必须可复现 (M6-T3)");
                    signatures.Add(faction.Personality + "|" + FactionBases.Signature(world));
                }
            }
            Assert.GreaterOrEqual(signatures.Count, 9, "9 套模板(3 性格 × 3 规模)");
        }

        [Test]
        public void DestroyedFactionBuilding_PersistsAsRuin_AfterFreezeThaw()
        {
            var universe = TestUtil.NewUniverse(165UL, 96);
            var redBanner = universe.FactionsSandbox.Get(FactionSystem.RedBannerId);
            universe.Bodies.TryGet("redridge", out var body);
            var world = FactionBases.Instantiate(universe.Seed, redBanner, body, 96);
            int buildingsBefore = world.Buildings.Count;

            // Destroy one structure, freeze (save), wait a day, thaw (restore).
            int victim = -1;
            foreach (var pair in world.Buildings.All)
            {
                if (pair.Value.DefId == BuildingDefs.TestBlockId)
                {
                    victim = pair.Key;
                }
            }
            Assert.AreNotEqual(-1, victim, "红旗兵营必须有可摧毁工事");
            world.Buildings.Remove(victim);
            byte[] frozen = SaveSerializer.ToGzipJson(SaveSerializer.Capture(world));

            var thawed = SaveSerializer.Restore(SaveSerializer.FromGzipJson(frozen));
            TestUtil.Run(thawed, GameConstants.TicksPerDay / 10);
            Assert.AreEqual(buildingsBefore - 1, thawed.Buildings.Count,
                "摧毁的建筑离开再回来仍是废墟 (M6-T4 差分持久化)");
            Assert.IsFalse(thawed.Buildings.TryGet(victim, out _));
        }
    }
}
