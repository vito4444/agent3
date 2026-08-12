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

        private static World BuildSampleWorld()
        {
            var world = new World(Seed, RegionSize);
            int center = RegionSize / 2;
            world.Commands.Enqueue(new PlaceBuildingCommand { DefId = BuildingDefs.TestBlockId, X = center, Y = center, Rotation = 0 });
            world.Commands.Enqueue(new PlaceBuildingCommand { DefId = BuildingDefs.TestBlockId, X = center + 4, Y = center, Rotation = 1 });
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

            byte[] blob = SaveSerializer.ToGzipJson(SaveSerializer.Capture(world));

            // Mutate the original after capture; the restored copy must match the capture.
            world.Commands.Enqueue(new RemoveBuildingCommand { BuildingId = 1 });
            world.Step();

            var restored = SaveSerializer.Restore(SaveSerializer.FromGzipJson(blob));
            Assert.AreEqual(hashBefore, restored.ComputeStateHash());
            Assert.AreEqual(WarmupTicks, restored.Tick);
            Assert.AreEqual(2, restored.Buildings.Count);
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
        public void RestoredWorld_ContinuesDeterministically()
        {
            var world = BuildSampleWorld();
            var restored = SaveSerializer.Restore(SaveSerializer.FromGzipJson(SaveSerializer.ToGzipJson(SaveSerializer.Capture(world))));

            const int extraTicks = 200;
            for (int i = 0; i < extraTicks; i++)
            {
                world.Step();
                restored.Step();
            }
            Assert.AreEqual(world.ComputeStateHash(), restored.ComputeStateHash());
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
