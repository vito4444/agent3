using System;
using System.Text;
using NUnit.Framework;
using Starsoil.BalanceSim.Scenarios;
using Starsoil.Core;

namespace Starsoil.BalanceSim.Tests
{
    /// <summary>Temporary M1 debugging probe; deleted once E1 stabilizes.</summary>
    [Explicit("diagnostic only")]
    public sealed class DiagnosticProbe
    {
        [Test]
        public void DumpE1Timeline()
        {
            var world = TestUtil.NewColonyWorld(E1Scenario.DefaultSeed, E1Scenario.RegionSize);
            E1Scenario.ApplyOpeningScript(world);
            var sb = new StringBuilder();
            long targetTicks = 3L * GameConstants.TicksPerDay;
            while (world.Tick < targetTicks && world.Colonists.AliveCount > 0)
            {
                if (world.Tick % GameConstants.TicksPerHour == 0)
                {
                    E1Scenario.TickOrders(world);
                    var campfire = world.Buildings.FindFirstOfKind(BuildingKind.Campfire);
                    sb.Append("d").Append(world.Day).Append(" h").Append(world.HourOfDay)
                      .Append(" alive=").Append(world.Colonists.AliveCount)
                      .Append(" tank=").Append(world.Life.TankO2.ToString("F0"))
                      .Append(" water=").Append(world.CountItemEverywhere(ItemIds.Water))
                      .Append(" ice=").Append(world.CountItemEverywhere(ItemIds.Ice))
                      .Append(" ration=").Append(world.CountItemEverywhere(ItemIds.Ration))
                      .Append(" tasks=").Append(world.Tasks.All.Count)
                      .Append(" bp=").Append(world.Blueprints.All.Count);
                    if (campfire != null)
                    {
                        int orders = world.Crafting.OrdersByStation.TryGetValue(campfire.Id, out var list) ? list.Count : 0;
                        var active = world.Crafting.ActiveOrder(world, campfire);
                        sb.Append(" [campfire ice=").Append(campfire.Stock.Get(ItemIds.Ice))
                          .Append(" inIce=").Append(campfire.Inbound.Get(ItemIds.Ice))
                          .Append(" water=").Append(campfire.Stock.Get(ItemIds.Water))
                          .Append(" orders=").Append(orders)
                          .Append(" active=").Append(active != null ? active.RecipeId : "none")
                          .Append("]");
                    }
                    else
                    {
                        sb.Append(" [no campfire]");
                    }
                    foreach (var c in world.Colonists.AllSorted())
                    {
                        sb.Append(" | c").Append(c.Id).Append(c.Alive ? "" : "(dead)")
                          .Append(" ").Append(c.Activity)
                          .Append(" o2=").Append(c.O2.ToString("F0"))
                          .Append(" btl=").Append(c.BottleO2.ToString("F0"))
                          .Append(" w=").Append(c.Water.ToString("F0"))
                          .Append(" f=").Append(c.Food.ToString("F0"))
                          .Append(" s=").Append(c.Sleep.ToString("F0"))
                          .Append(" t=").Append(c.Temp.ToString("F0"))
                          .Append(" @").Append(c.X).Append(",").Append(c.Y);
                    }
                    sb.AppendLine();
                }
                world.Step();
            }
            Console.WriteLine(sb.ToString());
        }
    }
}
