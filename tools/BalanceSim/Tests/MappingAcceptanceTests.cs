using NUnit.Framework;
using Starsoil.Core;

namespace Starsoil.BalanceSim.Tests
{
    /// <summary>
    /// M2-T9: the hand→machine mapping table (docs/plan/03), row by row for the 10 rows
    /// due at M2. Task-pool takeover is asserted behaviorally in MachineTests (mining)
    /// and BotTests (hauling); machine recipes run on machine stations only, so hand
    /// stations never generate those craft tasks. This suite pins the ≥4x throughput
    /// requirement for every hand/machine recipe pair plus generation and greenhouse rows.
    /// </summary>
    public sealed class MappingAcceptanceTests
    {
        private static readonly (string hand, string machine, string row)[] RecipePairs =
        {
            ("melt_ice", "m_purify_ice", "化水:篝火炉 → 水净化器"),
            ("iron_lump", "m_smelt_iron", "熔炼:篝火炉 → 熔炉(铁)"),
            ("copper_lump", "m_smelt_copper", "熔炼:篝火炉 → 熔炉(铜)"),
            ("crude_glass", "m_glass", "熔炼:篝火炉 → 熔炉(玻璃)"),
            ("carbon_powder", "m_crush_carbon", "破碎:手工台 → 破碎机(碳)"),
            ("salt_refine", "m_refine_salt", "破碎:手工台 → 破碎机(盐)"),
            ("plant_fiber", "m_fiber", "破碎:手工台 → 破碎机(纤维)"),
            ("emergency_ration", "m_ration", "压制:手工台 → 压制机(口粮)"),
            ("preserved_ration", "m_preserved", "压制:手工台 → 压制机(腌制)"),
            ("insulation_wrap", "m_insulation", "压制:手工台 → 压制机(保温垫)"),
            ("crude_tool", "m_tool", "组装:手工台 → 装配机(工具)"),
            ("bandage", "m_bandage", "组装:手工台 → 装配机(绷带)")
        };

        [Test]
        public void MachineRecipes_AreAtLeastFourTimesHandThroughput()
        {
            var world = TestUtil.NewColonyWorld(111UL, 96);
            foreach (var (hand, machine, row) in RecipePairs)
            {
                Assert.IsTrue(world.Crafting.TryGetRecipe(hand, out var handRecipe), hand);
                Assert.IsTrue(world.Crafting.TryGetRecipe(machine, out var machineRecipe), machine);
                float handPerUnit = handRecipe.WorkTicks * Balance.HandcraftTimeFactor / handRecipe.Outputs[0].Count;
                float machinePerUnit = machineRecipe.WorkTicks / (float)machineRecipe.Outputs[0].Count;
                Assert.GreaterOrEqual(handPerUnit / machinePerUnit, 4f,
                    "映射行不达 4 倍吞吐:" + row);
            }
        }

        [Test]
        public void Extraction_Generation_Greenhouse_Rows_MeetFourX()
        {
            // 采矿:手镐 300 ticks/单位 vs 采掘机 75 ticks/单位。
            Assert.GreaterOrEqual(Balance.MineTicksPerUnit / (float)Balance.ExtractorTicksPerUnit, 4f,
                "映射行不达 4 倍吞吐:手镐挖矿 → 采矿机");
            // 发电:手摇 1kW vs 太阳板 15kW。
            Assert.GreaterOrEqual(Balance.SolarPanelKw / Balance.HandCrankKw, 4f,
                "映射行不达 4 倍吞吐:手摇发电 → 太阳板");
            // 播种采收:徒手 200 ticks/生物质 vs 温室 400/8 = 50 ticks/生物质。
            var world = TestUtil.NewColonyWorld(112UL, 96);
            Assert.IsTrue(world.Crafting.TryGetRecipe("greenhouse_grow", out var grow));
            int biomassOut = 0;
            foreach (var output in grow.Outputs)
            {
                if (output.ItemId == ItemIds.Biomass)
                {
                    biomassOut = output.Count;
                }
            }
            float greenhousePerUnit = grow.WorkTicks / (float)biomassOut;
            Assert.GreaterOrEqual(Balance.GatherTicksPerUnit / greenhousePerUnit, 4f,
                "映射行不达 4 倍吞吐:手工播种采收 → 温室");
        }
    }
}
