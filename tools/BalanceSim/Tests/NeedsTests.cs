using System;
using NUnit.Framework;
using Starsoil.Core;

namespace Starsoil.BalanceSim.Tests
{
    /// <summary>
    /// M1-T6: need decay rates per the docs/plan/02 table, replenishment, the 1-game-hour
    /// critical window, bandage rescue, and death consequences.
    /// </summary>
    public sealed class NeedsTests
    {
        private const int RegionSize = 96;

        private static World NewWorld(ulong seed) => TestUtil.NewColonyWorld(seed, RegionSize);

        private static Colonist FirstColonist(World world) => world.Colonists.AllSorted()[0];

        [Test]
        public void WaterAndFood_DecayAt100PerDay()
        {
            var world = NewWorld(21UL);
            var colonist = FirstColonist(world);
            const int ticks = 1000;
            float expected = ticks * (Balance.NeedMax / GameConstants.TicksPerDay);
            float waterBefore = colonist.Water;
            float foodBefore = colonist.Food;
            TestUtil.Run(world, ticks);
            Assert.AreEqual(expected, waterBefore - colonist.Water, 0.01f, "water decay rate");
            Assert.AreEqual(expected, foodBefore - colonist.Food, 0.01f, "food decay rate");
        }

        [Test]
        public void Sleep_DecaysAt100Per16Hours()
        {
            var world = NewWorld(22UL);
            var colonist = FirstColonist(world);
            const int ticks = 1000;
            float expected = ticks * (Balance.NeedMax / (16f * GameConstants.TicksPerHour));
            float before = colonist.Sleep;
            TestUtil.Run(world, ticks);
            Assert.AreEqual(expected, before - colonist.Sleep, 0.01f, "sleep decay rate");
        }

        [Test]
        public void Temperature_DropsAtNight_Outdoors_AndRecoversIndoors()
        {
            var world = NewWorld(23UL);
            var colonist = FirstColonist(world);
            // Oversized bottle: no refill trips into the pod during the measurement.
            colonist.BottleO2 = 100000f;
            // Run to nightfall, keeping the colonist awake so no bed interrupt fires.
            bool night = TestUtil.RunUntil(world, 2 * GameConstants.TicksPerDay, w =>
            {
                colonist.Sleep = Balance.NeedMax;
                colonist.Water = Balance.NeedMax;
                colonist.Food = Balance.NeedMax;
                return w.IsNight;
            });
            Assert.IsTrue(night);
            // Park them idle on a known outdoor cell.
            world.Colonists.AbandonCurrent(world, colonist);
            colonist.Activity = ColonistActivity.Idle;
            colonist.X = world.StartX - 8;
            colonist.Y = world.StartY - 8;
            colonist.ClearPath();
            colonist.Temp = Balance.NeedMax;
            Assert.IsFalse(world.Buildings.IsCellInterior(colonist.X, colonist.Y),
                "colonist must be outdoors for this measurement");

            float atNightfall = colonist.Temp;
            for (int i = 0; i < GameConstants.TicksPerHour; i++)
            {
                colonist.Sleep = Balance.NeedMax;
                colonist.Water = Balance.NeedMax;
                colonist.Food = Balance.NeedMax;
                world.Step();
            }
            float drop = atNightfall - colonist.Temp;
            Assert.AreEqual(Balance.ColdLossPerTick * GameConstants.TicksPerHour, drop, 0.5f,
                "night outdoor temperature loss per hour");

            // Indoor recovery: move them into the pod interior and measure one hour.
            colonist.X = world.PodInteriorX;
            colonist.Y = world.PodInteriorY;
            float beforeRecovery = colonist.Temp;
            for (int i = 0; i < GameConstants.TicksPerHour && colonist.Temp < Balance.NeedMax; i++)
            {
                colonist.Sleep = Balance.NeedMax;
                world.Step();
            }
            Assert.Greater(colonist.Temp, beforeRecovery, "indoor temperature must recover");
        }

        [Test]
        public void Suffocation_KillsAfterExactlyOneGameHour_Critical()
        {
            var world = NewWorld(24UL);
            var colonist = FirstColonist(world);
            // Remove the pod so the tank stops regenerating and no refill can rescue them.
            var pod = world.Buildings.FindFirstOfKind(BuildingKind.CrashPod);
            world.Buildings.Remove(pod.Id);
            world.Life.TankO2 = 0f;
            colonist.BottleO2 = 0f;
            colonist.O2 = 0.0001f;

            world.Step();
            Assert.AreEqual(DeathCause.Suffocation, colonist.CriticalCause, "colonist should be critical");
            Assert.IsTrue(colonist.Alive);

            TestUtil.Run(world, Balance.CriticalDeathTicks - 2);
            Assert.IsTrue(colonist.Alive, "died before the 1-game-hour rescue window closed");

            TestUtil.Run(world, 4);
            Assert.IsFalse(colonist.Alive, "critical colonist survived past the rescue window");
            Assert.AreEqual(1, world.Piles.CountOf(ItemIds.Remains), "death must leave remains to bury");
        }

        [Test]
        public void Bandage_AutoConsumed_BuysTime()
        {
            var world = NewWorld(25UL);
            var colonist = FirstColonist(world);
            var pod = world.Buildings.FindFirstOfKind(BuildingKind.CrashPod);
            pod.Stock.Add(ItemIds.Bandage, 1);

            world.Life.TankO2 = 0f;
            colonist.BottleO2 = 0f;
            colonist.O2 = 0.0001f;
            world.Step();

            Assert.AreEqual(DeathCause.None, colonist.CriticalCause, "bandage should have prevented critical");
            Assert.GreaterOrEqual(colonist.O2, Balance.BandageNeedRestore - 1f, "bandage restores +30 of the need");
            Assert.AreEqual(0, pod.Stock.Get(ItemIds.Bandage), "bandage must be consumed");
        }

        [Test]
        public void Drinking_RefillsWater_FromStock()
        {
            var world = NewWorld(26UL);
            var colonist = FirstColonist(world);
            var pod = world.Buildings.FindFirstOfKind(BuildingKind.CrashPod);
            int before = pod.Stock.Get(ItemIds.Water);
            Assert.Greater(before, 0, "crash pod must start with water (docs/plan/02 inventory)");
            colonist.Water = Balance.DrinkAtThreshold - 1f;

            bool drank = TestUtil.RunUntil(world, 2000, w => colonist.Water > Balance.NeedMax - 5f);
            Assert.IsTrue(drank, "colonist never drank despite stocked water");
            Assert.Less(pod.Stock.Get(ItemIds.Water), before, "a water unit must be consumed");
        }

        [Test]
        public void Eating_RefillsFood_FromStartingRations()
        {
            var world = NewWorld(27UL);
            var colonist = FirstColonist(world);
            colonist.Food = Balance.EatAtThreshold - 1f;
            bool ate = TestUtil.RunUntil(world, 2000, w => colonist.Food > Balance.NeedMax - 5f);
            Assert.IsTrue(ate, "colonist never ate despite the crash pod rations");
        }

        [Test]
        public void Exhaustion_FaintsInsteadOfKilling()
        {
            var world = NewWorld(28UL);
            var colonist = FirstColonist(world);
            // Remove beds so no bed is claimable: unclaim by filling pod sleepers is
            // fiddly; instead push sleep straight to zero — the faint path must trigger.
            colonist.Sleep = 0.0001f;
            var pod = world.Buildings.FindFirstOfKind(BuildingKind.CrashPod);
            for (int i = 0; i < Balance.PodBeds; i++)
            {
                pod.SleepersIds.Add(1000 + i);
            }
            world.Step();
            world.Step();
            Assert.AreEqual(ColonistActivity.Fainted, colonist.Activity, "should faint at zero sleep");
            Assert.IsTrue(colonist.Alive, "exhaustion must not kill (docs/plan/02)");

            TestUtil.Run(world, Balance.FaintDurationTicks + 2);
            Assert.AreNotEqual(ColonistActivity.Fainted, colonist.Activity, "should wake after 2 game hours");
            Assert.AreEqual(Balance.WakeFromFaintSleep, colonist.Sleep, 1f);
        }
    }
}
