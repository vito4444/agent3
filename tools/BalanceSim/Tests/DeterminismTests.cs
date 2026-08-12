using System.Collections.Generic;
using NUnit.Framework;
using Starsoil.Core;

namespace Starsoil.BalanceSim.Tests
{
    /// <summary>
    /// M0-T3 acceptance: same seed + same command script over 10000 ticks must produce
    /// an identical world state hash on replay.
    /// </summary>
    public sealed class DeterminismTests
    {
        private const int RegionSize = 96;
        private const int TickCount = 10000;

        private static readonly (long tick, ICommand command)[] Script =
        {
            (3, new PlaceBuildingCommand { DefId = BuildingDefs.TestBlockId, X = 34, Y = 34, Rotation = 0 }),
            (10, new PlaceBuildingCommand { DefId = BuildingDefs.TestBlockId, X = 40, Y = 36, Rotation = 1 }),
            (20, new RemoveBuildingCommand { BuildingId = 1 }),
            (50, new PlaceBuildingCommand { DefId = BuildingDefs.TestBlockId, X = 36, Y = 44, Rotation = 2 }),
            (5000, new PlaceBuildingCommand { DefId = BuildingDefs.TestBlockId, X = 44, Y = 44, Rotation = 3 })
        };

        private static ulong RunScripted(ulong seed)
        {
            var world = new World(seed, RegionSize);
            var byTick = new Dictionary<long, List<ICommand>>();
            foreach (var (tick, command) in Script)
            {
                if (!byTick.TryGetValue(tick, out var list))
                {
                    list = new List<ICommand>();
                    byTick.Add(tick, list);
                }
                list.Add(command);
            }

            for (long t = 0; t < TickCount; t++)
            {
                if (byTick.TryGetValue(t, out var commands))
                {
                    foreach (var c in commands)
                    {
                        world.Commands.Enqueue(c);
                    }
                }
                world.Step();
            }
            return world.ComputeStateHash();
        }

        [Test]
        public void SameSeedSameScript_SameHash_Over10000Ticks()
        {
            const ulong seed = 42UL;
            ulong first = RunScripted(seed);
            ulong second = RunScripted(seed);
            Assert.AreEqual(first, second, "replay diverged: sim is not deterministic");
        }

        [Test]
        public void DifferentSeed_DifferentHash()
        {
            Assert.AreNotEqual(RunScripted(42UL), RunScripted(43UL));
        }

        [Test]
        public void CommandsMutateState_HashChanges()
        {
            const ulong seed = 42UL;
            var idle = new World(seed, RegionSize);
            for (long t = 0; t < TickCount; t++)
            {
                idle.Step();
            }
            Assert.AreNotEqual(idle.ComputeStateHash(), RunScripted(seed),
                "scripted commands had no effect on the state hash");
        }
    }
}
