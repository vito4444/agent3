using NUnit.Framework;
using Starsoil.Core;

namespace Starsoil.BalanceSim.Tests
{
    /// <summary>M2-T4/T9: hauler bots take over haul work, recharge at posts, and humans
    /// stop hauling while any bot is operational.</summary>
    public sealed class BotTests
    {
        private static World NewBotWorld(ulong seed, out int stationId)
        {
            var world = TestUtil.NewColonyWorld(seed, 96);
            int cx = world.StartX;
            int cy = world.StartY + 10;
            world.Buildings.Place(BuildingDefs.PowerPylonId, cx, cy, 0, out _);
            for (int i = 0; i < 3; i++)
            {
                world.Buildings.Place(BuildingDefs.SolarPanelId, cx - 6 + i * 3, cy + 4, 0, out _);
            }
            int battery = world.Buildings.Place(BuildingDefs.BatteryId, cx - 2, cy, 0, out _);
            world.Buildings.TryGet(battery, out var batteryState);
            batteryState.BatteryKwh = Balance.BatteryCapacityKwh;
            world.Buildings.Place(BuildingDefs.ChargingPostId, cx + 2, cy, 0, out _);
            stationId = world.Buildings.Place(BuildingDefs.BotStationId, cx + 4, cy, 0, out _);
            return world;
        }

        [Test]
        public void BotStation_SpawnsFourBots()
        {
            var world = NewBotWorld(91UL, out int stationId);
            world.Step();
            Assert.AreEqual(BotSystem.BotsPerStation, world.Bots.All.Count);
            foreach (var bot in world.Bots.AllSorted())
            {
                Assert.AreEqual(stationId, bot.HomeStationId);
            }
        }

        [Test]
        public void Bots_TakeHaulTasks_HumansDoNot()
        {
            var world = NewBotWorld(92UL, out _);
            world.Buildings.Place(BuildingDefs.SmallStorageId, world.StartX + 6, world.StartY + 14, 0, out _);
            for (int i = 0; i < 6; i++)
            {
                world.Piles.Drop(ItemIds.IronOre, 5, world.StartX - 8 + i, world.StartY + 12);
            }
            TestUtil.Run(world, Balance.DispatchIntervalTicks * 4);

            bool anyBotHauling = false;
            foreach (var bot in world.Bots.AllSorted())
            {
                if (bot.TaskId != 0)
                {
                    anyBotHauling = true;
                }
            }
            Assert.IsTrue(anyBotHauling, "bots must claim haul tasks");
            foreach (var colonist in world.Colonists.AllSorted())
            {
                if (colonist.TaskId != 0 && world.Tasks.TryGet(colonist.TaskId, out var task))
                {
                    Assert.IsFalse(task.Type == TaskType.HaulToStorage || task.Type == TaskType.HaulToStation ||
                                   task.Type == TaskType.HaulToBlueprint,
                        "colonists must not haul while bots are operational (M2-T9): colonist " + colonist.Id +
                        " has task " + task.Type + "#" + task.Id + " claimedBy=" + task.ClaimedBy);
                }
            }

            // Destination is whichever storage-capable building the dispatcher picked
            // (the crash pod is also a storage), so assert on total stored ore.
            bool delivered = TestUtil.RunUntil(world, 6000, w =>
            {
                int stored = 0;
                foreach (var pair in w.Buildings.All)
                {
                    if (BuildingDefs.TryGet(pair.Value.DefId, out var def) && def.StorageCapacity > 0)
                    {
                        stored += pair.Value.Stock.Get(ItemIds.IronOre);
                    }
                }
                return stored >= 30 && w.Piles.CountOf(ItemIds.IronOre) == 0;
            });
            Assert.IsTrue(delivered, "bots must move all loose ore into storage buildings");
        }

        [Test]
        public void Bot_Recharges_WhenBatteryLow()
        {
            var world = NewBotWorld(93UL, out _);
            world.Step();
            var bot = world.Bots.AllSorted()[0];
            bot.Battery = Balance.BotSeekChargeAt + 1f;
            // Give it endless work far away so it drains.
            world.Buildings.Place(BuildingDefs.SmallStorageId, world.StartX + 6, world.StartY + 14, 0, out _);
            for (int i = 0; i < 10; i++)
            {
                world.Piles.Drop(ItemIds.Carbon, 5, world.StartX - 10, world.StartY - 10);
            }
            bool charged = TestUtil.RunUntil(world, 20000, w =>
                bot.State == BotState.Charging || bot.Battery > Balance.BotSeekChargeAt + 10f);
            Assert.IsTrue(charged, "low-battery bot must reach a charging post and recharge");
        }

        [Test]
        public void HumansHaulAgain_WhenAllBotsDead()
        {
            var world = NewBotWorld(94UL, out _);
            world.Step();
            foreach (var bot in world.Bots.AllSorted())
            {
                bot.Battery = 0f;
            }
            // Remove charge post power by toggling posts off so bots stay down.
            foreach (var pair in world.Buildings.All)
            {
                if (pair.Value.DefId == BuildingDefs.ChargingPostId)
                {
                    pair.Value.WantsPower = false;
                }
            }
            world.Buildings.Place(BuildingDefs.SmallStorageId, world.StartX + 6, world.StartY + 14, 0, out _);
            world.Piles.Drop(ItemIds.IronOre, 10, world.StartX - 8, world.StartY + 12);
            TestUtil.Run(world, Balance.DispatchIntervalTicks * 4);

            bool humanHauls = false;
            foreach (var colonist in world.Colonists.AllSorted())
            {
                if (colonist.TaskId != 0 && world.Tasks.TryGet(colonist.TaskId, out var task) &&
                    task.Type == TaskType.HaulToStorage)
                {
                    humanHauls = true;
                }
            }
            Assert.IsTrue(humanHauls, "with every bot down, humans must haul again (机器停机回退)");
        }
    }
}
