using NUnit.Framework;
using Starsoil.Core;

namespace Starsoil.BalanceSim.Tests
{
    /// <summary>M2-T1/T2: power topology, priority shedding, battery curve, O2 network and pod standby.</summary>
    public sealed class NetworkTests
    {
        private static World NewWorld(ulong seed) => TestUtil.NewColonyWorld(seed, 96);

        private static int PlaceOk(World world, string defId, int x, int y)
        {
            int id = world.Buildings.Place(defId, x, y, 0, out var error);
            Assert.AreEqual(PlacementError.None, error, defId + " at " + x + "," + y);
            return id;
        }

        [Test]
        public void Pylons_LinkComponents_AndCoverBuildings()
        {
            var world = NewWorld(71UL);
            int cx = world.StartX;
            int cy = world.StartY + 10;
            int pylonA = PlaceOk(world, BuildingDefs.PowerPylonId, cx, cy);
            int pylonB = PlaceOk(world, BuildingDefs.PowerPylonId, cx + Balance.PowerPylonLinkRange, cy);
            int farPylon = PlaceOk(world, BuildingDefs.PowerPylonId, cx - Balance.PowerPylonLinkRange - 12, cy);
            int solar = PlaceOk(world, BuildingDefs.SolarPanelId, cx + Balance.PowerPylonLinkRange + Balance.PowerPylonCoverRadius - 1, cy);
            world.Step();

            Assert.AreEqual(world.Networks.PowerComponentOf(pylonA), world.Networks.PowerComponentOf(pylonB),
                "pylons within link range must join one component");
            Assert.AreNotEqual(0, world.Networks.PowerComponentOf(solar), "solar within cover radius must attach");
            Assert.AreEqual(world.Networks.PowerComponentOf(pylonA), world.Networks.PowerComponentOf(solar));
            Assert.AreNotEqual(world.Networks.PowerComponentOf(pylonA), world.Networks.PowerComponentOf(farPylon),
                "distant pylon must form its own component");
        }

        [Test]
        public void PowerShortage_ShedsByPriority_LifeSupportLast()
        {
            var world = NewWorld(72UL);
            int cx = world.StartX;
            int cy = world.StartY + 10;
            PlaceOk(world, BuildingDefs.PowerPylonId, cx, cy);
            // One wind turbine (≤14 kW) cannot feed purifier (10, LifeSupport) + miner-less
            // production load, so production sheds first.
            PlaceOk(world, BuildingDefs.WindTurbineId, cx + 1, cy);
            int purifier = PlaceOk(world, BuildingDefs.WaterPurifierId, cx + 3, cy);
            int press = PlaceOk(world, BuildingDefs.PressId, cx + 5, cy);
            world.Step();
            world.Step();

            world.Buildings.TryGet(purifier, out var purifierState);
            world.Buildings.TryGet(press, out var pressState);
            Assert.IsTrue(world.Networks.IsPowered(world, purifierState),
                "life-support purifier must be powered first");
            Assert.IsFalse(world.Networks.IsPowered(world, pressState),
                "production press must shed when supply is short");
        }

        [Test]
        public void Battery_ChargesFromSurplus_AndCoversDeficit()
        {
            var world = NewWorld(73UL);
            int cx = world.StartX;
            int cy = world.StartY + 10;
            PlaceOk(world, BuildingDefs.PowerPylonId, cx, cy);
            PlaceOk(world, BuildingDefs.SolarPanelId, cx + 2, cy);
            int batteryId = PlaceOk(world, BuildingDefs.BatteryId, cx - 2, cy);
            world.Buildings.TryGet(batteryId, out var battery);

            // Daytime surplus charges the battery.
            TestUtil.Run(world, GameConstants.TicksPerHour * 2);
            Assert.Greater(battery.BatteryKwh, 0f, "battery must charge from solar surplus");

            // Night + a life-support load: battery covers the deficit and drains.
            int purifier = PlaceOk(world, BuildingDefs.WaterPurifierId, cx + 4, cy);
            bool night = TestUtil.RunUntil(world, 2 * GameConstants.TicksPerDay, w => w.IsNight);
            Assert.IsTrue(night);
            float beforeNightDrain = battery.BatteryKwh;
            TestUtil.Run(world, GameConstants.TicksPerHour);
            world.Buildings.TryGet(purifier, out var purifierState);
            Assert.IsTrue(world.Networks.IsPowered(world, purifierState),
                "battery must keep life support powered at night");
            Assert.Less(battery.BatteryKwh, beforeNightDrain, "battery must drain covering the night deficit");
        }

        [Test]
        public void Electrolyzer_FillsGasNetwork_AndPodGoesStandby()
        {
            var world = NewWorld(74UL);
            int cx = world.StartX;
            int cy = world.StartY + 10;
            // Power for the electrolyzer.
            PlaceOk(world, BuildingDefs.PowerPylonId, cx, cy);
            PlaceOk(world, BuildingDefs.SolarPanelId, cx + 2, cy);
            PlaceOk(world, BuildingDefs.SolarPanelId, cx + 2, cy + 2);
            PlaceOk(world, BuildingDefs.SolarPanelId, cx + 2, cy - 3);
            PlaceOk(world, BuildingDefs.SolarPanelId, cx - 3, cy + 2);
            // Gas layer.
            PlaceOk(world, BuildingDefs.GasPylonId, cx + 4, cy);
            int electrolyzerId = PlaceOk(world, BuildingDefs.ElectrolyzerId, cx + 5, cy);
            PlaceOk(world, BuildingDefs.GasTankId, cx + 6, cy + 2);
            world.Buildings.TryGet(electrolyzerId, out var electrolyzer);
            electrolyzer.Stock.Add(ItemIds.Water, 20);

            // Four solar panels only clear the electrolyzer's 40 kW near midday.
            bool midday = TestUtil.RunUntil(world, GameConstants.TicksPerDay, w => w.HourOfDay == 12);
            Assert.IsTrue(midday);
            TestUtil.Run(world, GameConstants.TicksPerHour);

            int component = world.Networks.GasComponentOf(electrolyzerId);
            Assert.AreNotEqual(0, component, "electrolyzer must join the gas network");
            Assert.IsTrue(world.Networks.GasStored.TryGetValue(component, out float stored) && stored > 0f,
                "network must accumulate O2");
            Assert.Less(electrolyzer.Stock.Get(ItemIds.Water), 20, "electrolysis must consume water");
            Assert.IsTrue(world.Networks.PodIsBackup, "pod oxygen maker must drop to standby (M2-T2)");

            float tankBefore = world.Life.TankO2;
            TestUtil.Run(world, GameConstants.TicksPerHour);
            Assert.LessOrEqual(world.Life.TankO2, tankBefore,
                "standby pod must not keep producing while the electrolyzer runs");
        }

        [Test]
        public void SolarOutput_ZeroAtNight_PeaksMidday()
        {
            var world = NewWorld(75UL);
            bool midday = TestUtil.RunUntil(world, GameConstants.TicksPerDay, w => w.HourOfDay == 12);
            Assert.IsTrue(midday);
            Assert.Greater(NetworkSystem.SolarOutput(world), Balance.SolarPanelKw * 0.9f, "midday peak");
            bool night = TestUtil.RunUntil(world, GameConstants.TicksPerDay, w => w.IsNight);
            Assert.IsTrue(night);
            Assert.AreEqual(0f, NetworkSystem.SolarOutput(world), "no solar at night");
        }
    }
}
