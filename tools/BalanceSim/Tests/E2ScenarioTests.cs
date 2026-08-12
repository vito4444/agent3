using NUnit.Framework;
using Starsoil.BalanceSim.Scenarios;
using Starsoil.Core;

namespace Starsoil.BalanceSim.Tests
{
    /// <summary>
    /// M2-T10 acceptance: the pre-built machine base runs 24 game hours with oxygen,
    /// water, food and stored power all net non-negative, and zero manual labor tasks
    /// executed by colonists.
    /// </summary>
    public sealed class E2ScenarioTests
    {
        [Test]
        public void E2_ZeroManualLabor_SteadyState24h()
        {
            var world = E2Scenario.Build(E2Scenario.DefaultSeed);

            // Warm up one hour so production lines fill, then measure a full day.
            TestUtil.Run(world, GameConstants.TicksPerHour);
            float o2Start = TotalO2(world);
            int waterStart = world.CountItemEverywhere(ItemIds.Water);
            int foodStart = TotalFood(world);
            float powerStart = world.Networks.BatteryStoredKwh;

            int manualLaborSightings = 0;
            for (int hour = 0; hour < GameConstants.HoursPerDay; hour++)
            {
                for (int t = 0; t < GameConstants.TicksPerHour; t++)
                {
                    world.Step();
                    if (world.Tick % Balance.DispatchIntervalTicks == 0)
                    {
                        foreach (var colonist in world.Colonists.AllSorted())
                        {
                            if (colonist.TaskId != 0 && world.Tasks.TryGet(colonist.TaskId, out var task) &&
                                (task.Type == TaskType.Mine || task.Type == TaskType.HaulToStorage ||
                                 task.Type == TaskType.HaulToStation || task.Type == TaskType.HaulToBlueprint ||
                                 task.Type == TaskType.Build))
                            {
                                manualLaborSightings++;
                            }
                        }
                    }
                }
            }

            Assert.AreEqual(0, manualLaborSightings, "colonists performed manual labor in the machine base (M2-T10)");
            Assert.AreEqual(4, world.Colonists.AliveCount, "operators must survive the day");
            Assert.GreaterOrEqual(TotalO2(world), o2Start - 1f,
                "oxygen reserve must not shrink over the day (1-unit float/phase tolerance at full grid)");
            // Maintain orders oscillate in a hysteresis band around their target (a craft
            // fires only when stock dips below it), so item stocks are compared with a
            // one-batch tolerance; a real deficit would show up far beyond that.
            Assert.GreaterOrEqual(world.CountItemEverywhere(ItemIds.Water), waterStart - 2, "water stock must hold its band");
            Assert.GreaterOrEqual(TotalFood(world), foodStart - 1, "food stock must hold its band");
            Assert.GreaterOrEqual(world.Networks.BatteryStoredKwh, powerStart * 0.999f, "stored power must not shrink");
            Assert.Greater(world.Stats.CraftedOf(ItemIds.Water), 0, "water production must keep running");
            Assert.Greater(world.Stats.CraftedOf(ItemIds.Ration), 0, "food production must keep running");
        }

        private static float TotalO2(World world)
        {
            float total = world.Life.TankO2;
            foreach (var pair in world.Networks.GasStored)
            {
                total += pair.Value;
            }
            return total;
        }

        private static int TotalFood(World world)
        {
            int total = 0;
            foreach (string food in ItemIds.Foods)
            {
                total += world.CountItemEverywhere(food);
            }
            return total;
        }
    }
}
