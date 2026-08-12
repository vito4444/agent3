using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using NUnit.Framework;
using Starsoil.BalanceSim.Scenarios;
using Starsoil.Core;

namespace Starsoil.BalanceSim.Tests
{
    /// <summary>Schema v1 roundtrip with a live colony and the v0→v1 migration path.</summary>
    public sealed class SaveV1Tests
    {
        [Test]
        public void MidGameColony_RoundtripsWithEqualHash()
        {
            var world = TestUtil.NewColonyWorld(61UL, 96);
            E1Scenario.ApplyOpeningScript(world);
            TestUtil.Run(world, 3000);

            ulong before = world.ComputeStateHash();
            var restored = SaveSerializer.Restore(
                SaveSerializer.FromGzipJson(SaveSerializer.ToGzipJson(SaveSerializer.Capture(world))));
            Assert.AreEqual(before, restored.ComputeStateHash(), "colony roundtrip changed state");
            Assert.AreEqual(world.Colonists.AliveCount, restored.Colonists.AliveCount);
            Assert.AreEqual(world.Nodes.All.Count, restored.Nodes.All.Count);
            Assert.AreEqual(world.Life.TankO2, restored.Life.TankO2, 0.001f);
            Assert.AreEqual(world.Storm.NextStartTick, restored.Storm.NextStartTick);
        }

        [Test]
        public void RestoredColony_KeepsRunning()
        {
            var world = TestUtil.NewColonyWorld(62UL, 96);
            E1Scenario.ApplyOpeningScript(world);
            TestUtil.Run(world, 2000);
            var restored = SaveSerializer.Restore(
                SaveSerializer.FromGzipJson(SaveSerializer.ToGzipJson(SaveSerializer.Capture(world))));
            TestUtil.LoadTempRecipes(restored);
            TestUtil.Run(restored, 2000);
            Assert.Greater(restored.Colonists.AliveCount, 0, "restored colony died instantly");
        }

        [Test]
        public void V0Save_MigratesToV1()
        {
            const int size = 16;
            var heights = new byte[size * size];
            string json = "{\"SchemaVersion\":0,\"Seed\":42,\"Tick\":100,\"RegionSize\":" + size +
                          ",\"TerrainHeights\":\"" + Convert.ToBase64String(heights) + "\"," +
                          "\"Buildings\":[{\"Id\":1,\"DefId\":\"test_block\",\"X\":2,\"Y\":2,\"Rotation\":0}]}";
            byte[] blob = Gzip(json);

            var data = SaveSerializer.FromGzipJson(blob);
            Assert.AreEqual(GameConstants.SaveSchemaVersion, data.SchemaVersion, "migration must bump the version");

            var world = SaveSerializer.Restore(data);
            Assert.AreEqual(100, world.Tick);
            Assert.AreEqual(1, world.Buildings.Count);
            world.Buildings.TryGet(1, out var building);
            Assert.AreEqual(100f, building.Durability, 0.001f, "v0→v1 must default durability");
            Assert.AreEqual(0, world.Colonists.All.Count, "a v0 dev save has no colonists");
            // The migrated world must still tick without exceptions.
            TestUtil.Run(world, 50);
        }

        [Test]
        public void FutureSchema_IsRejected()
        {
            string json = "{\"SchemaVersion\":" + (GameConstants.SaveSchemaVersion + 1) + "}";
            Assert.Throws<InvalidDataException>(() => SaveSerializer.FromGzipJson(Gzip(json)));
        }

        private static byte[] Gzip(string json)
        {
            byte[] raw = Encoding.UTF8.GetBytes(json);
            using (var output = new MemoryStream())
            {
                using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
                {
                    gzip.Write(raw, 0, raw.Length);
                }
                return output.ToArray();
            }
        }
    }
}
