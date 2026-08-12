using System;
using System.Collections.Generic;

namespace Starsoil.Core
{
    /// <summary>
    /// Auto-working machines (docs/plan/03 generic machine model — no per-machine special
    /// logic beyond the two structural families):
    /// 1. Processing machines run craft orders exactly like hand stations, but without a
    ///    colonist, at full speed, gated by network power; inputs arrive via the same
    ///    HaulToStation tasks.
    /// 2. Extraction machines (miner / ice miner / forage station) pull units from a
    ///    covered deposit node into their output buffer.
    /// Work wears durability; below the threshold machines run at half speed until a
    ///    repair task applies repair gel (M2-T4).
    /// </summary>
    public sealed class MachineSystem
    {
        public void Tick(World world)
        {
            foreach (var pair in SortedBuildings(world))
            {
                var machine = pair;
                if (!BuildingDefs.TryGet(machine.DefId, out var def) || !def.IsMachine || !machine.WantsPower)
                {
                    continue;
                }
                if (!world.Networks.IsPowered(world, machine))
                {
                    continue;
                }
                if (def.Extracts.Count > 0)
                {
                    TickExtractor(world, machine, def);
                }
                else
                {
                    TickProcessor(world, machine, def);
                }
            }
        }

        private static List<BuildingState> SortedBuildings(World world)
        {
            var keys = new List<int>(world.Buildings.All.Keys);
            keys.Sort();
            var result = new List<BuildingState>(keys.Count);
            foreach (int key in keys)
            {
                result.Add(world.Buildings.All[key]);
            }
            return result;
        }

        private void TickExtractor(World world, BuildingState machine, BuildingDef def)
        {
            if (machine.Stock.TotalUnits() >= Balance.ExtractorOutputBufferCap)
            {
                return;
            }
            int nodeId = world.Buildings.FindDepositFor(def, machine.X, machine.Y);
            if (nodeId == 0 || !world.Nodes.TryGet(nodeId, out var node))
            {
                return;
            }
            float speed = SpeedFactor(machine);
            machine.ProcessAccum += speed;
            ApplyWear(machine, speed);
            if (machine.ProcessAccum < Balance.ExtractorTicksPerUnit)
            {
                return;
            }
            machine.ProcessAccum -= Balance.ExtractorTicksPerUnit;
            machine.Stock.Add(node.ItemId, 1);
            world.Stats.CountMined(node.ItemId, 1);
            if (!world.Nodes.ExtractUnit(node))
            {
                world.Events.Add(new NodeDepletedEvent { NodeId = node.Id, X = node.X, Y = node.Y });
            }
        }

        private void TickProcessor(World world, BuildingState machine, BuildingDef def)
        {
            var order = world.Crafting.ActiveOrder(world, machine);
            if (order == null || !world.Crafting.TryGetRecipe(order.RecipeId, out var recipe) ||
                !recipe.RunsOn(def.Kind, out bool handSpeed) || handSpeed)
            {
                machine.ProcessAccum = 0f;
                return;
            }
            if (!world.Crafting.InputsReady(machine, recipe))
            {
                return;
            }
            float speed = SpeedFactor(machine);
            machine.ProcessAccum += speed;
            ApplyWear(machine, speed);
            if (machine.ProcessAccum < recipe.WorkTicks)
            {
                return;
            }
            machine.ProcessAccum = 0f;
            world.Crafting.CompleteCraft(world, machine, order, recipe);
        }

        private static float SpeedFactor(BuildingState machine)
        {
            return machine.Durability < Balance.LowDurabilityThreshold
                ? Balance.MachineLowDurabilityFactor
                : 1f;
        }

        private static void ApplyWear(BuildingState machine, float workedTicks)
        {
            float wear = Balance.MachineWearPerWorkHour * workedTicks / GameConstants.TicksPerHour;
            machine.Durability = Math.Max(Balance.MinDurability, machine.Durability - wear);
        }
    }
}
