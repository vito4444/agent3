using NUnit.Framework;
using Starsoil.Core;

namespace Starsoil.BalanceSim.Tests
{
    /// <summary>M1-T8: sandstorm forecast lead, duration bounds, durability wear, task suspension.</summary>
    public sealed class StormTests
    {
        [Test]
        public void Forecast_ArrivesExactlyOneDayAhead_AndDurationInBounds()
        {
            var world = TestUtil.NewColonyWorld(51UL, 96);
            long scheduledStart = world.Storm.NextStartTick;
            Assert.Greater(scheduledStart, Balance.StormForecastLeadTicks, "first storm must leave forecast room");

            long forecastTick = -1;
            long startTick = -1;
            long endTick = -1;
            while (world.Tick < scheduledStart + Balance.StormMaxDurationTicks + 10 && endTick < 0)
            {
                world.Step();
                foreach (var evt in world.Events)
                {
                    switch (evt)
                    {
                        case StormForecastEvent forecast:
                            forecastTick = world.Tick;
                            Assert.AreEqual(scheduledStart, forecast.StartTick);
                            break;
                        case StormStartedEvent started:
                            startTick = world.Tick;
                            endTick = started.EndTick;
                            break;
                    }
                }
            }

            Assert.AreEqual(scheduledStart - Balance.StormForecastLeadTicks, forecastTick - 1,
                "forecast must land exactly one game day before the storm");
            Assert.AreEqual(scheduledStart, startTick - 1, "storm must start at the scheduled tick");
            long duration = endTick - (startTick - 1);
            Assert.That(duration, Is.InRange((long)Balance.StormMinDurationTicks, (long)Balance.StormMaxDurationTicks),
                "storm duration out of the 6-12h band: " + duration);
        }

        [Test]
        public void Storm_WearsExposedStations_AndSuspendsOutdoorDispatch()
        {
            var world = TestUtil.NewColonyWorld(52UL, 96);
            int stationId = world.Buildings.Place(BuildingDefs.WorkbenchId, world.StartX + 4, world.StartY + 4, 0, out _);
            world.Buildings.TryGet(stationId, out var station);

            // Jump to the storm by running the sim forward.
            bool stormStarted = TestUtil.RunUntil(world, world.Storm.NextStartTick + 10, w => w.Storm.Active);
            Assert.IsTrue(stormStarted, "storm never started");
            float durabilityAtStart = station.Durability;

            // Designate a node during the storm: the task may exist but must stay unclaimed.
            foreach (var node in world.Nodes.All)
            {
                node.Value.Designated = true;
                break;
            }
            TestUtil.Run(world, Balance.DispatchIntervalTicks * 3);
            foreach (var task in world.Tasks.All.Values)
            {
                if (task.Type == TaskType.Mine)
                {
                    Assert.AreEqual(0, task.ClaimedBy, "outdoor task dispatched during a storm (docs/plan/02)");
                }
            }

            TestUtil.Run(world, GameConstants.TicksPerHour);
            Assert.Less(station.Durability, durabilityAtStart, "exposed station must wear during the storm");
            float expectedLoss = Balance.StormDurabilityLossPerHour;
            Assert.AreEqual(expectedLoss, durabilityAtStart - station.Durability, 0.2f,
                "durability wear should be ≈2 per game hour");
        }
    }
}
