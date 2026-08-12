using System;
using System.Collections.Generic;

namespace Starsoil.Core
{
    public enum TaskType
    {
        Build,
        HaulToBlueprint,
        HaulToStation,
        HaulToStorage,
        Craft,
        Mine,
        Crank,
        Bury,
        Repair
    }

    public sealed class WorkTask
    {
        public int Id;
        public TaskType Type;
        public int Priority;
        public int TargetX;
        public int TargetY;
        public int BlueprintId;
        public int StationId;
        public int OrderId;
        public int NodeId;
        public int BuildingId;
        public string ItemId;
        public int Count;
        /// <summary>Haul source: exactly one of pile / building id is set.</summary>
        public int SourcePileId;
        public int SourceBuildingId;
        public int ClaimedBy;
        /// <summary>All M1 work happens outdoors; storms suspend outdoor tasks (docs/plan/02).</summary>
        public bool Outdoor = true;
    }

    /// <summary>
    /// Region task pool + dispatch (docs/plan/03): tasks are generated from world needs
    /// every dispatch round and assigned "highest priority first, then nearest idle worker".
    /// A task is claimed by at most one colonist (M1-T3 acceptance).
    /// </summary>
    public sealed class TaskSystem
    {
        public const int PriorityBuild = 80;
        public const int PriorityHaulBlueprint = 70;
        public const int PriorityHaulStation = 65;
        public const int PriorityCraft = 60;
        public const int PriorityRepair = 58;
        public const int PriorityBury = 50;
        /// <summary>Mining an item some station/blueprint is currently starving for.</summary>
        public const int PriorityMineDemand = 55;
        public const int PriorityMine = 40;
        public const int PriorityCrank = 30;
        public const int PriorityHaulStorage = 20;
        public const int CarryCapacity = 10;

        private readonly Dictionary<int, WorkTask> _tasks = new Dictionary<int, WorkTask>();
        private int _nextId = 1;

        public IReadOnlyDictionary<int, WorkTask> All => _tasks;

        public bool TryGet(int id, out WorkTask task) => _tasks.TryGetValue(id, out task);

        public void Remove(World world, WorkTask task, bool releaseReservations)
        {
            if (releaseReservations)
            {
                ReleaseSourceReservation(world, task);
                ReleaseDestinationReservation(world, task);
            }
            _tasks.Remove(task.Id);
        }

        public void Unclaim(WorkTask task)
        {
            task.ClaimedBy = 0;
        }

        /// <summary>Runs once per Balance.DispatchIntervalTicks from World.Step.</summary>
        public void GenerateAndDispatch(World world)
        {
            GenerateBlueprintTasks(world);
            GenerateStationTasks(world);
            GenerateMineTasks(world);
            GenerateCrankTasks(world);
            GenerateBuryTasks(world);
            GenerateRepairTasks(world);
            GenerateOutputHaulTasks(world);
            GenerateStorageTasks(world);
            Dispatch(world);
        }

        /// <summary>Machines below the durability threshold get a repair-gel task (M2-T4).</summary>
        private void GenerateRepairTasks(World world)
        {
            foreach (var building in SortedValues(world.Buildings.All))
            {
                if (!building.NeedsRepair ||
                    HasTaskFor(t => t.Type == TaskType.Repair && t.BuildingId == building.Id))
                {
                    continue;
                }
                if (!TryReserveSource(world, ItemIds.RepairGel, 1, PriorityRepair,
                        out int pileId, out int sourceBuildingId, out int reserved, excludeBuildingId: building.Id))
                {
                    continue;
                }
                AddTask(new WorkTask
                {
                    Type = TaskType.Repair,
                    Priority = PriorityRepair,
                    TargetX = building.X,
                    TargetY = building.Y,
                    BuildingId = building.Id,
                    ItemId = ItemIds.RepairGel,
                    Count = reserved,
                    SourcePileId = pileId,
                    SourceBuildingId = sourceBuildingId
                });
            }
        }

        /// <summary>Hauls finished goods out of station/machine buffers into storage.</summary>
        private void GenerateOutputHaulTasks(World world)
        {
            foreach (var station in SortedValues(world.Buildings.All))
            {
                if (!BuildingDefs.TryGet(station.DefId, out var def) ||
                    (!def.IsStation && !def.IsMachine) || def.StorageCapacity > 0 ||
                    def.Kind == BuildingKind.ResearchBench)
                {
                    continue;
                }
                var keepItems = CollectStationInputs(world, station, def);
                foreach (var entry in station.Stock.SortedEntries())
                {
                    if (keepItems.Contains(entry.Key))
                    {
                        continue;
                    }
                    int available = entry.Value - station.Reserved.Get(entry.Key);
                    if (available <= 0 ||
                        HasTaskFor(t => t.Type == TaskType.HaulToStorage &&
                                        t.SourceBuildingId == station.Id && t.ItemId == entry.Key))
                    {
                        continue;
                    }
                    var storage = FindStorageWithSpace(world, entry.Key);
                    if (storage == null)
                    {
                        continue;
                    }
                    int chunk = Math.Min(available, CarryCapacity);
                    station.Reserved.Add(entry.Key, chunk);
                    AddTask(new WorkTask
                    {
                        Type = TaskType.HaulToStorage,
                        Priority = PriorityHaulStorage,
                        TargetX = storage.X,
                        TargetY = storage.Y,
                        BuildingId = storage.Id,
                        ItemId = entry.Key,
                        Count = chunk,
                        SourceBuildingId = station.Id
                    });
                }
            }
        }

        /// <summary>Items this station's queued orders still consume (they stay in the buffer).</summary>
        private static HashSet<string> CollectStationInputs(World world, BuildingState station, BuildingDef def)
        {
            var keep = new HashSet<string>();
            if (world.Crafting.OrdersByStation.TryGetValue(station.Id, out var orders))
            {
                foreach (var order in orders)
                {
                    if (world.Crafting.TryGetRecipe(order.RecipeId, out var recipe))
                    {
                        foreach (var input in recipe.Inputs)
                        {
                            keep.Add(input.ItemId);
                        }
                    }
                }
            }
            return keep;
        }

        private void GenerateBlueprintTasks(World world)
        {
            foreach (var bp in SortedValues(world.Blueprints.All))
            {
                BuildingDefs.TryGet(bp.DefId, out var def);
                if (!bp.MaterialsComplete)
                {
                    foreach (var need in def.BuildCost)
                    {
                        int missing = world.Blueprints.MissingUnreserved(bp, need.ItemId);
                        while (missing > 0)
                        {
                            int chunk = Math.Min(missing, CarryCapacity);
                            if (!TryReserveSource(world, need.ItemId, chunk, PriorityHaulBlueprint,
                                    out int pileId, out int buildingId, out int reserved))
                            {
                                break;
                            }
                            bp.InboundReserved.Add(need.ItemId, reserved);
                            AddTask(new WorkTask
                            {
                                Type = TaskType.HaulToBlueprint,
                                Priority = PriorityHaulBlueprint,
                                TargetX = bp.X,
                                TargetY = bp.Y,
                                BlueprintId = bp.Id,
                                ItemId = need.ItemId,
                                Count = reserved,
                                SourcePileId = pileId,
                                SourceBuildingId = buildingId
                            });
                            missing -= reserved;
                        }
                    }
                }
                else if (!HasTaskFor(t => t.Type == TaskType.Build && t.BlueprintId == bp.Id))
                {
                    AddTask(new WorkTask
                    {
                        Type = TaskType.Build,
                        Priority = PriorityBuild,
                        TargetX = bp.X,
                        TargetY = bp.Y,
                        BlueprintId = bp.Id
                    });
                }
            }
        }

        private void GenerateStationTasks(World world)
        {
            foreach (var station in SortedValues(world.Buildings.All))
            {
                if (!BuildingDefs.TryGet(station.DefId, out var def) || !def.IsStation)
                {
                    continue;
                }
                var order = world.Crafting.ActiveOrder(world, station);
                if (order == null || !world.Crafting.TryGetRecipe(order.RecipeId, out var recipe))
                {
                    continue;
                }
                if (recipe.Station != def.Kind)
                {
                    continue;
                }
                if (!world.Crafting.InputsReady(station, recipe))
                {
                    foreach (var input in recipe.Inputs)
                    {
                        int missing = world.Crafting.MissingInput(station, recipe, input.ItemId);
                        while (missing > 0)
                        {
                            int chunk = Math.Min(missing, CarryCapacity);
                            if (!TryReserveSource(world, input.ItemId, chunk, PriorityHaulStation,
                                    out int pileId, out int buildingId, out int reserved, excludeBuildingId: station.Id))
                            {
                                break;
                            }
                            station.Inbound.Add(input.ItemId, reserved);
                            AddTask(new WorkTask
                            {
                                Type = TaskType.HaulToStation,
                                Priority = PriorityHaulStation,
                                TargetX = station.X,
                                TargetY = station.Y,
                                StationId = station.Id,
                                ItemId = input.ItemId,
                                Count = reserved,
                                SourcePileId = pileId,
                                SourceBuildingId = buildingId
                            });
                            missing -= reserved;
                        }
                    }
                }
                else if (!HasTaskFor(t => t.Type == TaskType.Craft && t.StationId == station.Id))
                {
                    AddTask(new WorkTask
                    {
                        Type = TaskType.Craft,
                        Priority = PriorityCraft,
                        TargetX = station.X,
                        TargetY = station.Y,
                        StationId = station.Id,
                        OrderId = order.Id
                    });
                }
            }
        }

        private void GenerateMineTasks(World world)
        {
            var demanded = CollectDemandedItems(world);
            var machineCovered = CollectExtractorTargets(world);
            foreach (var node in SortedValues(world.Nodes.All))
            {
                if (!node.Designated || node.Remaining <= 0)
                {
                    continue;
                }
                if (machineCovered.Contains(node.Id))
                {
                    // A powered extraction machine works this deposit; hand mining stops
                    // (docs/plan/03 hand-to-machine mapping, M2-T9). It resumes if the
                    // machine loses power or is toggled off.
                    RemoveMineTaskFor(world, node.Id);
                    continue;
                }
                int priority = demanded.Contains(node.ItemId) ? PriorityMineDemand : PriorityMine;
                WorkTask existing = null;
                foreach (var task in _tasks.Values)
                {
                    if (task.Type == TaskType.Mine && task.NodeId == node.Id)
                    {
                        existing = task;
                        break;
                    }
                }
                if (existing != null)
                {
                    existing.Priority = priority;
                }
                else
                {
                    AddTask(new WorkTask
                    {
                        Type = TaskType.Mine,
                        Priority = priority,
                        TargetX = node.X,
                        TargetY = node.Y,
                        NodeId = node.Id
                    });
                }
            }
        }

        private static HashSet<int> CollectExtractorTargets(World world)
        {
            var covered = new HashSet<int>();
            foreach (var building in world.Buildings.All.Values)
            {
                if (!BuildingDefs.TryGet(building.DefId, out var def) || !def.IsMachine ||
                    def.Extracts.Count == 0 || !building.WantsPower)
                {
                    continue;
                }
                if (!world.Networks.IsPowered(world, building))
                {
                    continue;
                }
                int nodeId = world.Buildings.FindDepositFor(def, building.X, building.Y);
                if (nodeId != 0)
                {
                    covered.Add(nodeId);
                }
            }
            return covered;
        }

        private void RemoveMineTaskFor(World world, int nodeId)
        {
            WorkTask existing = null;
            foreach (var task in _tasks.Values)
            {
                if (task.Type == TaskType.Mine && task.NodeId == nodeId && task.ClaimedBy == 0)
                {
                    existing = task;
                    break;
                }
            }
            if (existing != null)
            {
                Remove(world, existing, releaseReservations: false);
            }
        }

        /// <summary>Items that active craft orders or blueprints are currently short of,
        /// counting all stock anywhere (demand-driven mining priority).</summary>
        private static HashSet<string> CollectDemandedItems(World world)
        {
            var demanded = new HashSet<string>();
            foreach (var station in world.Buildings.All.Values)
            {
                if (!BuildingDefs.TryGet(station.DefId, out var def) || !def.IsStation)
                {
                    continue;
                }
                var order = world.Crafting.ActiveOrder(world, station);
                if (order == null || !world.Crafting.TryGetRecipe(order.RecipeId, out var recipe))
                {
                    continue;
                }
                foreach (var input in recipe.Inputs)
                {
                    if (world.CountItemEverywhere(input.ItemId) < input.Count * 2)
                    {
                        demanded.Add(input.ItemId);
                    }
                }
            }
            foreach (var bp in world.Blueprints.All.Values)
            {
                if (!BuildingDefs.TryGet(bp.DefId, out var def))
                {
                    continue;
                }
                foreach (var need in def.BuildCost)
                {
                    if (bp.Delivered.Get(need.ItemId) < need.Count &&
                        world.CountItemEverywhere(need.ItemId) < need.Count)
                    {
                        demanded.Add(need.ItemId);
                    }
                }
            }
            return demanded;
        }

        private void GenerateCrankTasks(World world)
        {
            foreach (var building in SortedValues(world.Buildings.All))
            {
                if (building.DefId == BuildingDefs.HandCrankId && building.StaffedRequested &&
                    !HasTaskFor(t => t.Type == TaskType.Crank && t.BuildingId == building.Id))
                {
                    AddTask(new WorkTask
                    {
                        Type = TaskType.Crank,
                        Priority = PriorityCrank,
                        TargetX = building.X,
                        TargetY = building.Y,
                        BuildingId = building.Id
                    });
                }
            }
        }

        private void GenerateBuryTasks(World world)
        {
            foreach (var pile in SortedValues(world.Piles.All))
            {
                if (pile.ItemId == ItemIds.Remains && pile.Available > 0 &&
                    !HasTaskFor(t => t.Type == TaskType.Bury && t.SourcePileId == pile.Id))
                {
                    pile.Reserved += 1;
                    AddTask(new WorkTask
                    {
                        Type = TaskType.Bury,
                        Priority = PriorityBury,
                        TargetX = world.GraveX,
                        TargetY = world.GraveY,
                        ItemId = ItemIds.Remains,
                        Count = 1,
                        SourcePileId = pile.Id
                    });
                }
            }
        }

        private void GenerateStorageTasks(World world)
        {
            foreach (var pile in SortedValues(world.Piles.All))
            {
                if (pile.ItemId == ItemIds.Remains || pile.Available <= 0)
                {
                    continue;
                }
                if (HasTaskFor(t => t.Type == TaskType.HaulToStorage && t.SourcePileId == pile.Id))
                {
                    continue;
                }
                var storage = FindStorageWithSpace(world, pile.ItemId);
                if (storage == null)
                {
                    continue;
                }
                int chunk = Math.Min(pile.Available, CarryCapacity);
                pile.Reserved += chunk;
                AddTask(new WorkTask
                {
                    Type = TaskType.HaulToStorage,
                    Priority = PriorityHaulStorage,
                    TargetX = storage.X,
                    TargetY = storage.Y,
                    BuildingId = storage.Id,
                    ItemId = pile.ItemId,
                    Count = chunk,
                    SourcePileId = pile.Id
                });
            }
        }

        private static BuildingState FindStorageWithSpace(World world, string itemId)
        {
            foreach (var building in SortedValues(world.Buildings.All))
            {
                if (!BuildingDefs.TryGet(building.DefId, out var def) || def.StorageCapacity <= 0)
                {
                    continue;
                }
                if (building.Stock.TotalUnits() < def.StorageCapacity)
                {
                    return building;
                }
            }
            return null;
        }

        private void Dispatch(World world)
        {
            var unclaimed = new List<WorkTask>();
            foreach (var task in SortedValues(_tasks))
            {
                if (task.ClaimedBy == 0 && !(world.Storm.Active && task.Outdoor))
                {
                    unclaimed.Add(task);
                }
            }
            unclaimed.Sort((a, b) => a.Priority != b.Priority ? b.Priority.CompareTo(a.Priority) : a.Id.CompareTo(b.Id));

            var idle = new List<Colonist>();
            foreach (var colonist in world.Colonists.AllSorted())
            {
                if (colonist.Alive && colonist.Activity == ColonistActivity.Idle)
                {
                    idle.Add(colonist);
                }
            }

            foreach (var task in unclaimed)
            {
                if (idle.Count == 0)
                {
                    break;
                }
                int bestIndex = -1;
                int bestDistance = int.MaxValue;
                for (int i = 0; i < idle.Count; i++)
                {
                    int distance = Math.Abs(idle[i].X - task.TargetX) + Math.Abs(idle[i].Y - task.TargetY);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestIndex = i;
                    }
                }
                var worker = idle[bestIndex];
                idle.RemoveAt(bestIndex);
                task.ClaimedBy = worker.Id;
                worker.AssignTask(task);
            }
        }

        public void ReleaseSourceReservation(World world, WorkTask task)
        {
            if (task.Count <= 0)
            {
                return;
            }
            if (task.SourcePileId != 0 && world.Piles.TryGet(task.SourcePileId, out var pile))
            {
                pile.Reserved = Math.Max(0, pile.Reserved - task.Count);
            }
            else if (task.SourceBuildingId != 0 && world.Buildings.TryGet(task.SourceBuildingId, out var building))
            {
                building.Reserved.Add(task.ItemId, -Math.Min(task.Count, building.Reserved.Get(task.ItemId)));
            }
        }

        public void ReleaseDestinationReservation(World world, WorkTask task)
        {
            if (task.Count <= 0)
            {
                return;
            }
            if (task.Type == TaskType.HaulToBlueprint && world.Blueprints.TryGet(task.BlueprintId, out var bp))
            {
                bp.InboundReserved.Add(task.ItemId, -Math.Min(task.Count, bp.InboundReserved.Get(task.ItemId)));
            }
            else if (task.Type == TaskType.HaulToStation && world.Buildings.TryGet(task.StationId, out var station))
            {
                station.Inbound.Add(task.ItemId, -Math.Min(task.Count, station.Inbound.Get(task.ItemId)));
            }
        }

        /// <summary>
        /// Reserves up to <paramref name="wanted"/> units from the best stock source.
        /// When nothing is free, unclaimed lower-priority tasks holding reservations on
        /// the item are cancelled and the reservation is retried — otherwise a stale
        /// storage haul can starve a blueprint or station forever.
        /// </summary>
        private bool TryReserveSource(World world, string itemId, int wanted, int requesterPriority,
            out int pileId, out int buildingId, out int reserved, int excludeBuildingId = 0)
        {
            if (TryReserveSourceOnce(world, itemId, wanted, out pileId, out buildingId, out reserved, excludeBuildingId))
            {
                return true;
            }
            if (ReleaseUnclaimedHoldersBelow(world, itemId, requesterPriority))
            {
                return TryReserveSourceOnce(world, itemId, wanted, out pileId, out buildingId, out reserved, excludeBuildingId);
            }
            return false;
        }

        private static bool TryReserveSourceOnce(World world, string itemId, int wanted,
            out int pileId, out int buildingId, out int reserved, int excludeBuildingId)
        {
            pileId = 0;
            buildingId = 0;
            reserved = 0;
            foreach (var pile in SortedValues(world.Piles.All))
            {
                if (pile.ItemId == itemId && pile.Available > 0)
                {
                    reserved = Math.Min(wanted, pile.Available);
                    pile.Reserved += reserved;
                    pileId = pile.Id;
                    return true;
                }
            }
            foreach (var building in SortedValues(world.Buildings.All))
            {
                if (building.Id == excludeBuildingId)
                {
                    continue;
                }
                int available = building.Stock.Get(itemId) - building.Reserved.Get(itemId);
                if (available > 0)
                {
                    reserved = Math.Min(wanted, available);
                    building.Reserved.Add(itemId, reserved);
                    buildingId = building.Id;
                    return true;
                }
            }
            return false;
        }

        /// <summary>Cancels unclaimed lower-priority haul tasks that reserve the item.</summary>
        private bool ReleaseUnclaimedHoldersBelow(World world, string itemId, int requesterPriority)
        {
            var toRelease = new List<WorkTask>();
            foreach (var task in _tasks.Values)
            {
                if (task.ClaimedBy == 0 && task.ItemId == itemId && task.Count > 0 &&
                    task.Priority < requesterPriority &&
                    (task.SourcePileId != 0 || task.SourceBuildingId != 0))
                {
                    toRelease.Add(task);
                }
            }
            toRelease.Sort((a, b) => a.Id.CompareTo(b.Id));
            foreach (var task in toRelease)
            {
                Remove(world, task, releaseReservations: true);
            }
            return toRelease.Count > 0;
        }

        private bool HasTaskFor(Func<WorkTask, bool> predicate)
        {
            foreach (var task in _tasks.Values)
            {
                if (predicate(task))
                {
                    return true;
                }
            }
            return false;
        }

        private void AddTask(WorkTask task)
        {
            task.Id = _nextId;
            _nextId++;
            _tasks.Add(task.Id, task);
        }

        private static List<T> SortedValues<T>(IReadOnlyDictionary<int, T> map)
        {
            var keys = new List<int>(map.Keys);
            keys.Sort();
            var result = new List<T>(keys.Count);
            foreach (int key in keys)
            {
                result.Add(map[key]);
            }
            return result;
        }
    }
}
