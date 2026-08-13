using System.Collections.Generic;

namespace Starsoil.Core
{
    /// <summary>
    /// All player intents enter the sim as commands applied at the start of a tick;
    /// UI never mutates sim state directly (docs/plan/08).
    /// </summary>
    public interface ICommand
    {
        void Execute(World world);
    }

    public sealed class CommandQueue
    {
        private readonly Queue<ICommand> _pending = new Queue<ICommand>();

        public int PendingCount => _pending.Count;

        public void Enqueue(ICommand command)
        {
            _pending.Enqueue(command);
        }

        internal void Drain(World world)
        {
            while (_pending.Count > 0)
            {
                _pending.Dequeue().Execute(world);
            }
        }
    }

    /// <summary>Debug/test-only instant placement (M0 path). Player construction goes
    /// through PlaceBlueprintCommand.</summary>
    public sealed class PlaceBuildingCommand : ICommand
    {
        public string DefId;
        public int X;
        public int Y;
        public int Rotation;

        public void Execute(World world)
        {
            int id = world.Buildings.Place(DefId, X, Y, Rotation, out PlacementError error);
            if (error == PlacementError.None)
            {
                world.Events.Add(new BuildingPlacedEvent
                {
                    BuildingId = id,
                    DefId = DefId,
                    X = X,
                    Y = Y,
                    Rotation = Rotation
                });
            }
            else
            {
                world.Events.Add(new CommandRejectedEvent { Reason = "place:" + error });
            }
        }
    }

    public sealed class RemoveBuildingCommand : ICommand
    {
        public int BuildingId;

        public void Execute(World world)
        {
            if (world.Buildings.Remove(BuildingId))
            {
                world.Events.Add(new BuildingRemovedEvent { BuildingId = BuildingId });
            }
            else
            {
                world.Events.Add(new CommandRejectedEvent { Reason = "remove:not_found" });
            }
        }
    }

    /// <summary>Player construction: places a blueprint that must be hauled and built.
    /// Locked tech rejects the placement (M2-T11 gating).</summary>
    public sealed class PlaceBlueprintCommand : ICommand
    {
        public string DefId;
        public int X;
        public int Y;
        public int Rotation;

        public void Execute(World world)
        {
            if (BuildingDefs.TryGet(DefId, out var def) && !world.Tech.IsBuildingUnlocked(def))
            {
                world.Events.Add(new CommandRejectedEvent { Reason = "blueprint:" + PlacementError.Locked });
                return;
            }
            int id = world.Blueprints.Place(DefId, X, Y, Rotation, out PlacementError error);
            if (error == PlacementError.None)
            {
                world.Events.Add(new BlueprintPlacedEvent
                {
                    BlueprintId = id,
                    DefId = DefId,
                    X = X,
                    Y = Y,
                    Rotation = Rotation
                });
            }
            else
            {
                world.Events.Add(new CommandRejectedEvent { Reason = "blueprint:" + error });
            }
        }
    }

    public sealed class CancelBlueprintCommand : ICommand
    {
        public int BlueprintId;

        public void Execute(World world)
        {
            if (world.Blueprints.Cancel(BlueprintId, world.Piles))
            {
                world.Events.Add(new BlueprintRemovedEvent { BlueprintId = BlueprintId });
            }
        }
    }

    /// <summary>Demolish refunds 50% of the build cost (docs/plan/03).</summary>
    public sealed class DemolishBuildingCommand : ICommand
    {
        public int BuildingId;

        public void Execute(World world)
        {
            if (!world.Buildings.TryGet(BuildingId, out var building) ||
                !BuildingDefs.TryGet(building.DefId, out var def) || !def.Demolishable)
            {
                world.Events.Add(new CommandRejectedEvent { Reason = "demolish:not_allowed" });
                return;
            }
            foreach (var entry in building.Stock.SortedEntries())
            {
                world.Piles.Drop(entry.Key, entry.Value, building.X, building.Y);
            }
            foreach (var need in def.BuildCost)
            {
                int refund = (int)(need.Count * Balance.DemolishRefundFactor);
                if (refund > 0)
                {
                    world.Piles.Drop(need.ItemId, refund, building.X, building.Y);
                }
            }
            world.Buildings.Remove(BuildingId);
            world.Events.Add(new BuildingRemovedEvent { BuildingId = BuildingId });
        }
    }

    public sealed class ToggleNodeDesignationCommand : ICommand
    {
        public int NodeId;
        public bool Designated;

        public void Execute(World world)
        {
            if (world.Nodes.TryGet(NodeId, out var node))
            {
                node.Designated = Designated;
            }
        }
    }

    public sealed class AddCraftOrderCommand : ICommand
    {
        public int StationId;
        public string RecipeId;
        /// <summary>Craft count; -1 with MaintainTarget for maintain orders.</summary>
        public int Count = 1;
        public int MaintainTarget;

        public void Execute(World world)
        {
            if (!world.Tech.IsRecipeUnlocked(RecipeId))
            {
                world.Events.Add(new CommandRejectedEvent { Reason = "craft_order:locked" });
                return;
            }
            if (world.Buildings.TryGet(StationId, out _) && world.Crafting.TryGetRecipe(RecipeId, out _))
            {
                world.Crafting.AddOrder(StationId, RecipeId, Count, MaintainTarget);
            }
            else
            {
                world.Events.Add(new CommandRejectedEvent { Reason = "craft_order:invalid" });
            }
        }
    }

    public sealed class SetResearchTargetCommand : ICommand
    {
        public string NodeId;

        public void Execute(World world)
        {
            if (world.Tech.CanSelectTarget(NodeId))
            {
                world.Tech.ResearchTarget = NodeId;
                world.Tech.PaidCores = new Inventory();
            }
            else
            {
                world.Events.Add(new CommandRejectedEvent { Reason = "research:invalid_target" });
            }
        }
    }

    public sealed class SetJobQuotasCommand : ICommand
    {
        public List<KeyValuePair<string, int>> Quotas = new List<KeyValuePair<string, int>>();

        public void Execute(World world)
        {
            foreach (var pair in Quotas)
            {
                if (System.Enum.TryParse(pair.Key, out JobType job))
                {
                    world.Jobs.Quotas[job] = System.Math.Max(0, pair.Value);
                }
            }
            world.Jobs.ReassignJobs(world);
        }
    }

    public sealed class SetJobMatrixCommand : ICommand
    {
        public string Job;
        public string Task;
        public int Priority;

        public void Execute(World world)
        {
            if (System.Enum.TryParse(Job, out JobType job) && System.Enum.TryParse(Task, out TaskType task))
            {
                world.Jobs.SetMatrix(job, task, Priority);
            }
        }
    }

    public sealed class RemoveCraftOrderCommand : ICommand
    {
        public int StationId;
        public int OrderId;

        public void Execute(World world)
        {
            world.Crafting.RemoveOrder(StationId, OrderId);
        }
    }

    public sealed class SetCrankStaffedCommand : ICommand
    {
        public int BuildingId;
        public bool Staffed;

        public void Execute(World world)
        {
            if (world.Buildings.TryGet(BuildingId, out var building) &&
                building.DefId == BuildingDefs.HandCrankId)
            {
                building.StaffedRequested = Staffed;
            }
        }
    }

    /// <summary>Configures a launch pad's rocket order (M4). Parts and payload are then
    /// hauled in by logistics; the Universe launches at the next window.</summary>
    public sealed class SetPadOrderCommand : ICommand
    {
        public int PadId;
        public string TargetBodyId = string.Empty;
        public string Payload = Universe.CargoPodPayload;
        public int Crew;
        public List<Ingredient> Cargo = new List<Ingredient>();

        public void Execute(World world)
        {
            if (!world.Buildings.TryGet(PadId, out var pad) || pad.DefId != BuildingDefs.LaunchPadId)
            {
                world.Events.Add(new CommandRejectedEvent { Reason = "pad_order:not_a_pad" });
                return;
            }
            pad.Pad = new PadOrder
            {
                TargetBodyId = TargetBodyId,
                Payload = Payload,
                Crew = Crew,
                Cargo = Cargo,
                Active = true
            };
        }
    }

    /// <summary>Deploys a combat bot from stock as a live unit (M7 战斗蛛).</summary>
    public sealed class DeployCombatBotCommand : ICommand
    {
        public int X;
        public int Y;
        public bool Armored;

        public void Execute(World world)
        {
            if (world.Battle.DeployPlayerBot(world, X, Y, Armored) == null)
            {
                world.Events.Add(new CommandRejectedEvent { Reason = "deploy:cap_or_stock" });
            }
        }
    }

    /// <summary>三指令: 驻守(点)、巡逻(两点)、集结(旗) (docs/plan/07).</summary>
    public sealed class SetUnitOrderCommand : ICommand
    {
        public int UnitId;
        public UnitOrder Order;
        public int Ax;
        public int Ay;
        public int Bx;
        public int By;

        public void Execute(World world)
        {
            world.Battle.SetOrder(UnitId, Order, Ax, Ay, Bx, By);
        }
    }

    public sealed class SetRallyFlagCommand : ICommand
    {
        public int X;
        public int Y;

        public void Execute(World world)
        {
            world.Battle.RallyX = X;
            world.Battle.RallyY = Y;
        }
    }

    public sealed class RetreatAllCommand : ICommand
    {
        public void Execute(World world)
        {
            world.Battle.RetreatAll(world);
        }
    }

    public sealed class SetTutorialSkippedCommand : ICommand
    {
        public bool Skipped;

        public void Execute(World world)
        {
            world.Tutorial.Skipped = Skipped;
        }
    }
}
