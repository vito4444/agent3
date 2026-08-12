using System.Collections.Generic;
using System.Globalization;

namespace Starsoil.Core
{
    public sealed class BodyDef
    {
        public string Id;
        public string Zh;
        public string En;
        public string Type;
        public bool Landable;
        public float Gravity;
        public int DayHours;
        public bool NightCold;
        public float SolarFactor;
        public int OrbitIndex;
        public List<string> Resources = new List<string>();
    }

    /// <summary>The 曦光 system catalog (data/celestial_bodies.csv, docs/plan/02).</summary>
    public sealed class BodyCatalog
    {
        private readonly Dictionary<string, BodyDef> _bodies = new Dictionary<string, BodyDef>();

        public IReadOnlyDictionary<string, BodyDef> All => _bodies;

        public bool TryGet(string id, out BodyDef body) => _bodies.TryGetValue(id, out body);

        public void LoadFromCsv(IEnumerable<string> lines)
        {
            _bodies.Clear();
            string[] header = null;
            foreach (string raw in lines)
            {
                string line = raw.TrimEnd('\r');
                if (line.Length == 0 || line.TrimStart().StartsWith("#"))
                {
                    continue;
                }
                var cells = SplitCsv(line);
                if (header == null)
                {
                    header = cells.ToArray();
                    continue;
                }
                string Get(string column)
                {
                    for (int i = 0; i < header.Length && i < cells.Count; i++)
                    {
                        if (header[i].Trim() == column)
                        {
                            return cells[i].Trim();
                        }
                    }
                    return string.Empty;
                }
                string id = Get("id");
                if (id.Length == 0)
                {
                    continue;
                }
                var body = new BodyDef
                {
                    Id = id,
                    Zh = Get("zh"),
                    En = Get("en"),
                    Type = Get("type"),
                    Landable = Get("landable") == "1",
                    Gravity = ParseFloat(Get("gravity"), 1f),
                    DayHours = ParseInt(Get("day_hours"), GameConstants.HoursPerDay),
                    NightCold = Get("night_cold") == "1",
                    SolarFactor = ParseFloat(Get("solar_factor"), 1f),
                    OrbitIndex = ParseInt(Get("orbit_index"), 1)
                };
                foreach (string resource in Get("resources").Split(';'))
                {
                    if (resource.Trim().Length > 0)
                    {
                        body.Resources.Add(resource.Trim());
                    }
                }
                _bodies[id] = body;
            }
        }

        private static float ParseFloat(string value, float fallback)
        {
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)
                ? parsed : fallback;
        }

        private static int ParseInt(string value, int fallback)
        {
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                ? parsed : fallback;
        }

        private static List<string> SplitCsv(string line)
        {
            var cells = new List<string>();
            var current = new System.Text.StringBuilder();
            bool quoted = false;
            foreach (char c in line)
            {
                if (quoted)
                {
                    if (c == '"')
                    {
                        quoted = false;
                    }
                    else
                    {
                        current.Append(c);
                    }
                }
                else if (c == '"')
                {
                    quoted = true;
                }
                else if (c == ',')
                {
                    cells.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
            cells.Add(current.ToString());
            return cells;
        }
    }
}
