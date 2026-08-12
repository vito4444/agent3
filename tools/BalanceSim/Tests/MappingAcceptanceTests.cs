using NUnit.Framework;
using Starsoil.Core;

namespace Starsoil.BalanceSim.Tests
{
    /// <summary>
    /// M2-T9: the hand→machine mapping table (docs/plan/03), row by row. Since the M3
    /// catalog bridge, one recipe runs on both its hand station (×HandcraftTimeFactor
    /// time) and its machine station (×1), so the ≥4x throughput requirement is the
    /// factor itself plus dedicated rows for extraction, generation and the greenhouse.
    /// Task-pool takeover stays behaviorally asserted in MachineTests and BotTests.
    /// </summary>
    public sealed class MappingAcceptanceTests
    {
        /// <summary>(recipe, hand kind, machine kind) — one row per mapping-table line.</summary>
        private static readonly (string id, BuildingKind hand, BuildingKind machine, string row)[] Rows =
        {
            ("make_water_melt", BuildingKind.Campfire, BuildingKind.Furnace, "化水:篝火炉 → 熔炉/净化器"),
            ("make_iron_lump", BuildingKind.Campfire, BuildingKind.Furnace, "熔炼:篝火炉 → 熔炉(粗铁)"),
            ("smelt_iron", BuildingKind.Campfire, BuildingKind.Furnace, "熔炼:篝火炉 → 熔炉(铁锭)"),
            ("make_carbon_powder", BuildingKind.Workbench, BuildingKind.Crusher, "破碎:手工台 → 破碎机(碳)"),
            ("make_salt", BuildingKind.Workbench, BuildingKind.Crusher, "破碎:手工台 → 破碎机(盐)"),
            ("make_fiber", BuildingKind.Workbench, BuildingKind.Crusher, "破碎:手工台 → 破碎机(纤维)"),
            ("make_ration", BuildingKind.Workbench, BuildingKind.Press, "压制:手工台 → 压制机(口粮)"),
            ("make_preserved_ration", BuildingKind.Workbench, BuildingKind.Press, "压制:手工台 → 压制机(腌制)"),
            ("make_insulation_wrap", BuildingKind.Workbench, BuildingKind.Press, "压制:手工台 → 压制机(保温垫)"),
            ("make_crude_tool", BuildingKind.Workbench, BuildingKind.Assembler, "组装:手工台 → 装配机(工具)"),
            ("make_bandage", BuildingKind.Workbench, BuildingKind.Assembler, "组装:手工台 → 装配机(绷带)")
        };

        [Test]
        public void HandVerbRecipes_RunOnBothStations_AtFourXFactor()
        {
            Assert.GreaterOrEqual(Balance.HandcraftTimeFactor, 4f,
                "the hand penalty must carry the ≥4x machine throughput requirement (M2-T9)");
            var world = TestUtil.NewColonyWorld(111UL, 96);
            foreach (var (id, hand, machine, row) in Rows)
            {
                Assert.IsTrue(world.Crafting.TryGetRecipe(id, out var recipe), "missing recipe " + id);
                Assert.IsTrue(recipe.RunsOn(hand, out bool handSpeed) && handSpeed,
                    "映射行手工站不匹配:" + row);
                Assert.IsTrue(recipe.RunsOn(machine, out bool machineSpeed) && !machineSpeed,
                    "映射行机器站不匹配:" + row);
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
            Assert.IsTrue(world.Crafting.TryGetRecipe("make_grow_biomass", out var grow));
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
