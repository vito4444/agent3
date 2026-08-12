using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Newtonsoft.Json;

namespace Starsoil.Core
{
    /// <summary>
    /// gzip(JSON) save pipeline (docs/plan/08). Binary is only considered if measured
    /// main-thread stalls exceed the plan's 200ms trigger.
    /// </summary>
    public static class SaveSerializer
    {
        public static SaveData Capture(World world)
        {
            var data = new SaveData
            {
                Seed = world.Seed,
                Tick = world.Tick,
                RegionSize = world.Terrain.Size,
                TerrainHeights = world.Terrain.CopyHeights()
            };
            var ids = new List<int>(world.Buildings.All.Keys);
            ids.Sort();
            for (int i = 0; i < ids.Count; i++)
            {
                var b = world.Buildings.All[ids[i]];
                data.Buildings.Add(new SavedBuilding
                {
                    Id = b.Id,
                    DefId = b.DefId,
                    X = b.X,
                    Y = b.Y,
                    Rotation = b.Rotation
                });
            }
            return data;
        }

        public static World Restore(SaveData data)
        {
            if (data.SchemaVersion != GameConstants.SaveSchemaVersion)
            {
                // The migration registry arrives with the first schema bump (docs/plan/08).
                throw new InvalidDataException(
                    "Unsupported save schema " + data.SchemaVersion + ", expected " + GameConstants.SaveSchemaVersion);
            }
            var terrain = TerrainGrid.FromHeights(data.RegionSize, data.TerrainHeights);
            var world = new World(data.Seed, terrain);
            world.RestoreTick(data.Tick);
            world.Buildings.RestoreFrom(data.Buildings);
            return world;
        }

        public static byte[] ToGzipJson(SaveData data)
        {
            string json = JsonConvert.SerializeObject(data, Formatting.None);
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

        public static SaveData FromGzipJson(byte[] bytes)
        {
            using (var input = new MemoryStream(bytes))
            using (var gzip = new GZipStream(input, CompressionMode.Decompress))
            using (var reader = new StreamReader(gzip, Encoding.UTF8))
            {
                string json = reader.ReadToEnd();
                return JsonConvert.DeserializeObject<SaveData>(json);
            }
        }

        public static void WriteFile(string path, SaveData data)
        {
            File.WriteAllBytes(path, ToGzipJson(data));
        }

        public static SaveData ReadFile(string path)
        {
            return FromGzipJson(File.ReadAllBytes(path));
        }
    }
}
