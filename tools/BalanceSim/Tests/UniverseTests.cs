using System.Collections.Generic;
using NUnit.Framework;
using Starsoil.Core;

namespace Starsoil.BalanceSim.Tests
{
    /// <summary>M4: rockets, transits, new-region landing, abstract regions, region switching
    /// and universe save/load.</summary>
    public sealed class UniverseTests
    {
        private static Universe NewLaunchReadyUniverse(ulong seed, out int padId)
        {
            var universe = TestUtil.NewUniverse(seed, 96);
            var world = universe.ActiveWorld;
            world.Tech.UnlockAll();
            padId = world.Buildings.Place(BuildingDefs.LaunchPadId, world.StartX + 6, world.StartY + 2, 0, out var err);
            Assert.AreEqual(PlacementError.None, err, "launch pad placement");
            return universe;
        }

        private static void StockPad(World world, int padId, string payload)
        {
            world.Buildings.TryGet(padId, out var pad);
            foreach (var part in Universe.RocketParts)
            {
                pad.Stock.Add(part.ItemId, part.Count);
            }
            pad.Stock.Add(Universe.PayloadItem(payload), 1);
        }

        [Test]
        public void Launch_Transit_Arrival_OpensNewRegion()
        {
            var universe = NewLaunchReadyUniverse(121UL, out int padId);
            var world = universe.ActiveWorld;
            int aliveBefore = world.Colonists.AliveCount;

            // Pre-stock everything on the pad, then order: logistics sees no gap and the
            // home larder stays untouched (hauling itself is covered by station tests).
            StockPad(world, padId, Universe.ColonistPodPayload);
            world.Buildings.TryGet(padId, out var pad);
            pad.Stock.Add("ration", 20);
            pad.Stock.Add("water", 20);
            world.Commands.Enqueue(new SetPadOrderCommand
            {
                PadId = padId,
                TargetBodyId = "palewatch",
                Payload = Universe.ColonistPodPayload,
                Crew = 2,
                Cargo = new List<Ingredient>
                {
                    new Ingredient { ItemId = "ration", Count = 20 },
                    new Ingredient { ItemId = "water", Count = 20 }
                }
            });
            universe.Step();

            bool launched = false;
            for (int i = 0; i < GameConstants.TicksPerHour * 7 && !launched; i++)
            {
                universe.Step();
                foreach (var evt in universe.ActiveWorld.Events)
                {
                    if (evt is RocketLaunchedEvent)
                    {
                        launched = true;
                    }
                }
            }
            Assert.IsTrue(launched, "rocket must lift off at a launch window");
            Assert.AreEqual(aliveBefore - 2, world.Colonists.AliveCount, "crew must board (leave the region)");
            Assert.AreEqual(1, universe.Transits.Count);

            // Palewatch is 1 travel day away.
            for (int i = 0; i < GameConstants.TicksPerDay + GameConstants.TicksPerHour; i++)
            {
                universe.Step();
            }
            Assert.AreEqual(0, universe.Transits.Count, "transit must arrive");
            Assert.AreEqual(1, universe.FrozenRegions.Count, "arrival must open a frozen region");
            foreach (var slot in universe.FrozenRegions.Values)
            {
                Assert.AreEqual("palewatch", slot.BodyId);
                Assert.AreEqual(2, slot.ColonistCount, "crew must land");
                Assert.IsTrue(slot.Stockpile.TryGetValue("ration", out int rations) && rations >= 20,
                    "cargo must land with the pod");
            }
        }

        [Test]
        public void SwitchActive_ThawsRegion_AndReturnTripPreservesState()
        {
            var universe = NewLaunchReadyUniverse(122UL, out int padId);
            var world = universe.ActiveWorld;
            StockPad(world, padId, Universe.ColonistPodPayload);
            world.Buildings.TryGet(padId, out var pad);
            pad.Stock.Add("ration", 30);
            world.Commands.Enqueue(new SetPadOrderCommand
            {
                PadId = padId,
                TargetBodyId = "palewatch",
                Payload = Universe.ColonistPodPayload,
                Crew = 2,
                Cargo = new List<Ingredient> { new Ingredient { ItemId = "ration", Count = 30 } }
            });
            universe.Step();
            for (int i = 0; i < GameConstants.TicksPerDay + GameConstants.TicksPerHour * 7; i++)
            {
                universe.Step();
            }
            Assert.AreEqual(1, universe.FrozenRegions.Count);
            int newRegionId = 0;
            foreach (int id in universe.FrozenRegions.Keys)
            {
                newRegionId = id;
            }

            universe.SwitchActive(newRegionId);
            Assert.AreEqual(newRegionId, universe.ActiveRegionId);
            Assert.AreEqual("palewatch", universe.ActiveBodyId);
            Assert.AreEqual(2, universe.ActiveWorld.Colonists.AliveCount, "crew must be alive on the new world");
            Assert.IsNotNull(universe.ActiveWorld.Body);
            Assert.AreEqual("palewatch", universe.ActiveWorld.Body.Id);
            Assert.Greater(universe.ActiveWorld.Nodes.All.Count, 0, "landing region must have body deposits");
            foreach (var node in universe.ActiveWorld.Nodes.All.Values)
            {
                Assert.AreNotEqual(ItemIds.Biomass, node.ItemId, "palewatch has no biomass shrubs");
            }

            universe.SwitchActive(1);
            Assert.AreEqual(1, universe.ActiveRegionId);
            Assert.AreEqual("dustloam", universe.ActiveBodyId);
            var deathDump = new System.Text.StringBuilder();
            foreach (var pair in universe.ActiveWorld.Stats.Deaths)
            {
                deathDump.Append(pair.Key).Append("x").Append(pair.Value).Append(" ");
            }
            Assert.Greater(universe.ActiveWorld.Colonists.AliveCount, 0,
                "home region must thaw intact; totalColonists=" + universe.ActiveWorld.Colonists.All.Count +
                " deaths=" + deathDump + " day=" + universe.ActiveWorld.Day);
        }

        [Test]
        public void AbstractRegion_ShortageEscalates_ToCasualties()
        {
            var universe = TestUtil.NewUniverse(123UL, 96);
            var slot = new RegionSlot
            {
                Id = 7,
                BodyId = "palewatch",
                Seed = 7UL,
                FrozenSave = SaveSerializer.ToGzipJson(SaveSerializer.Capture(
                    World.CreateLandingRegion(7UL, 64, null, 2, new List<Ingredient>()))),
                ColonistCount = 2
            };
            slot.Stockpile["water"] = 3;
            slot.Baseline["water"] = 3;
            slot.RatesPerHour["water"] = -1f;
            universe.FrozenRegions.Add(7, slot);

            // 3 hours of stock, then >24h of shortage → one pending casualty.
            for (int i = 0; i < GameConstants.TicksPerHour * 30; i++)
            {
                universe.Step();
            }
            Assert.AreEqual(0, slot.Stockpile["water"], "abstract stock must drain to zero");
            Assert.GreaterOrEqual(slot.PendingDeaths, 1, "sustained shortage must cost a colonist (docs/plan/06)");
        }

        [Test]
        public void Universe_SaveLoad_Roundtrip()
        {
            var universe = NewLaunchReadyUniverse(124UL, out int padId);
            var world = universe.ActiveWorld;
            StockPad(world, padId, Universe.ColonistPodPayload);
            world.Buildings.TryGet(padId, out var pad);
            pad.Stock.Add("water", 12);
            world.Commands.Enqueue(new SetPadOrderCommand
            {
                PadId = padId,
                TargetBodyId = "palewatch",
                Payload = Universe.ColonistPodPayload,
                Crew = 2,
                Cargo = new List<Ingredient> { new Ingredient { ItemId = "water", Count = 12 } }
            });
            universe.Step();
            for (int i = 0; i < GameConstants.TicksPerDay + GameConstants.TicksPerHour * 7; i++)
            {
                universe.Step();
            }
            Assert.AreEqual(1, universe.FrozenRegions.Count);

            byte[] blob = universe.ToGzipJson();
            var loaded = Universe.FromGzipJson(blob);
            loaded.Bodies.LoadFromCsv(System.IO.File.ReadAllLines(
                System.IO.Path.Combine(TestUtil.FindDataDir(), "celestial_bodies.csv")));

            Assert.AreEqual(universe.ActiveRegionId, loaded.ActiveRegionId);
            Assert.AreEqual(universe.ActiveWorld.ComputeStateHash(), loaded.ActiveWorld.ComputeStateHash(),
                "active world must roundtrip bit-identically");
            Assert.AreEqual(1, loaded.FrozenRegions.Count);
            foreach (var pair in universe.FrozenRegions)
            {
                var restored = loaded.FrozenRegions[pair.Key];
                Assert.AreEqual(pair.Value.BodyId, restored.BodyId);
                Assert.AreEqual(pair.Value.ColonistCount, restored.ColonistCount);
            }
        }
    }
}
