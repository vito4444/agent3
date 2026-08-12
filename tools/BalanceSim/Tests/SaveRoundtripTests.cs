using System.IO;
using NUnit.Framework;
using Starsoil.Core;

namespace Starsoil.BalanceSim.Tests
{
    /// <summary>M0-T6 acceptance: save → mutate → load restores the exact saved state.</summary>
    public sealed class SaveRoundtripTests
    {
        private const ulong Seed = 123UL;
        private const int RegionSize = 96;
        private const int WarmupTicks = 500;
        // M0-T6 acceptance: roundtrip a world holding 20 placed test buildings.
        private const int BuildingCount = 20;
        private const int BuildingsPerRow = 10;
        private const int FirstColumn = 26;
        private const int ColumnStride = 4;
        private const int FirstRow = 30;
        private const int RowStride = 8;

        private static World BuildSampleWorld()
        {
            var world = new World(Seed, RegionSize);
            for (int i = 0; i < BuildingCount; i++)
            {
                world.Commands.Enqueue(new PlaceBuildingCommand
                {
                    DefId = BuildingDefs.TestBlockId,
                    X = FirstColumn + (i % BuildingsPerRow) * ColumnStride,
                    Y = FirstRow + (i / BuildingsPerRow) * RowStride,
                    Rotation = i % 4
                });
            }
            for (int i = 0; i < WarmupTicks; i++)
            {
                world.Step();
            }
            return world;
        }

        [Test]
        public void GzipJson_Roundtrip_PreservesStateHash()
        {
            var world = BuildSampleWorld();
            ulong hashBefore = world.ComputeStateHash();
            var captured = SaveSerializer.Capture(world);

            byte[] blob = SaveSerializer.ToGzipJson(captured);

            // Mutate the original after capture; the restored copy must match the capture.
            world.Commands.Enqueue(new RemoveBuildingCommand { BuildingId = 2 });
            world.Step();

            var restored = SaveSerializer.Restore(SaveSerializer.FromGzipJson(blob));
            Assert.AreEqual(hashBefore, restored.ComputeStateHash());
            Assert.AreEqual(captured.Tick, restored.Tick);
            // 20 placed test blocks + the starting crash pod.
            Assert.AreEqual(BuildingCount + 1, restored.Buildings.Count);

            // Field-by-field comparison against the captured snapshot (M0-T6 acceptance).
            Assert.AreEqual(captured.Seed, restored.Seed);
            Assert.AreEqual(captured.RegionSize, restored.Terrain.Size);
            foreach (var saved in captured.Buildings)
            {
                Assert.IsTrue(restored.Buildings.TryGet(saved.Id, out var state), "missing building " + saved.Id);
                Assert.AreEqual(saved.DefId, state.DefId);
                Assert.AreEqual(saved.X, state.X);
                Assert.AreEqual(saved.Y, state.Y);
                Assert.AreEqual(saved.Rotation, state.Rotation);
            }
            for (int y = 0; y < restored.Terrain.Size; y++)
            {
                for (int x = 0; x < restored.Terrain.Size; x++)
                {
                    if (world.Terrain.GetHeight(x, y) != restored.Terrain.GetHeight(x, y))
                    {
                        Assert.Fail("terrain mismatch at " + x + "," + y);
                    }
                }
            }
        }

        [Test]
        public void FileRoundtrip_Works()
        {
            var world = BuildSampleWorld();
            string path = Path.Combine(Path.GetTempPath(), "starsoil_save_test.json.gz");
            try
            {
                SaveSerializer.WriteFile(path, SaveSerializer.Capture(world));
                var restored = SaveSerializer.Restore(SaveSerializer.ReadFile(path));
                Assert.AreEqual(world.ComputeStateHash(), restored.ComputeStateHash());
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        [Test]
        public void SameSnapshot_LoadedTwice_ContinuesIdentically()
        {
            // Transient AI state (paths, claimed tasks) is deliberately not saved, so a
            // live world and its snapshot may evolve differently. What must hold is that
            // two restores of the same snapshot evolve identically (docs/plan/08).
            var world = BuildSampleWorld();
            byte[] blob = SaveSerializer.ToGzipJson(SaveSerializer.Capture(world));
            var a = SaveSerializer.Restore(SaveSerializer.FromGzipJson(blob));
            var b = SaveSerializer.Restore(SaveSerializer.FromGzipJson(blob));

            const int extraTicks = 200;
            for (int i = 0; i < extraTicks; i++)
            {
                a.Step();
                b.Step();
            }
            Assert.AreEqual(a.ComputeStateHash(), b.ComputeStateHash());
        }

        [Test]
        public void UnsupportedSchema_Throws()
        {
            var data = SaveSerializer.Capture(BuildSampleWorld());
            data.SchemaVersion = GameConstants.SaveSchemaVersion + 1;
            Assert.Throws<InvalidDataException>(() => SaveSerializer.Restore(data));
        }
    }
}
