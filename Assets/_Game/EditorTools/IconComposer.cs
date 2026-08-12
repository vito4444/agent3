using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Starsoil.EditorTools
{
    /// <summary>
    /// IconComposer (M3-T4, docs/plan/05 icon grammar): composes an icon per catalog item
    /// as material base color + form glyph + verb badge, writing a PNG atlas plus a JSON
    /// uv-map into Assets/GeneratedIcons. High-frequency items get hand-drawn overrides by
    /// dropping same-named PNGs into Assets/IconOverrides (M8 art pass).
    /// </summary>
    public static class IconComposer
    {
        private const int IconSize = 32;
        private const int AtlasColumns = 20;
        private const string OutputFolder = "Assets/GeneratedIcons";
        private const string OverrideFolder = "Assets/IconOverrides";

        [MenuItem("Starsoil/Compose Item Icons")]
        public static void Run()
        {
            string itemsPath = Path.Combine(Application.dataPath, "..", "GeneratedData", "items.json");
            if (!File.Exists(itemsPath))
            {
                Debug.LogError("[IconComposer] GeneratedData/items.json missing; run RecipeGen first.");
                return;
            }
            var root = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(itemsPath));
            var items = new List<(string id, string spec, int tier)>();
            foreach (var token in root["items"])
            {
                if (token is Newtonsoft.Json.Linq.JObject item)
                {
                    items.Add((item.Value<string>("Id"),
                        item.Value<string>("IconSpec") ?? "auto",
                        item.Value<int?>("Tier") ?? 0));
                }
            }
            items.Sort((a, b) => string.CompareOrdinal(a.id, b.id));

            int rows = Mathf.CeilToInt(items.Count / (float)AtlasColumns);
            var atlas = new Texture2D(AtlasColumns * IconSize, rows * IconSize, TextureFormat.RGBA32, false);
            var uvMap = new System.Text.StringBuilder("{\n");

            for (int i = 0; i < items.Count; i++)
            {
                int cx = (i % AtlasColumns) * IconSize;
                int cy = (i / AtlasColumns) * IconSize;
                DrawIcon(atlas, cx, cy, items[i].id, items[i].spec, items[i].tier);
                uvMap.Append("  \"").Append(items[i].id).Append("\": [").Append(cx).Append(", ").Append(cy).Append("]");
                uvMap.Append(i < items.Count - 1 ? ",\n" : "\n");
            }
            uvMap.Append("}\n");

            atlas.Apply(false);
            Directory.CreateDirectory(OutputFolder);
            File.WriteAllBytes(Path.Combine(OutputFolder, "item_atlas.png"), atlas.EncodeToPNG());
            File.WriteAllText(Path.Combine(OutputFolder, "item_atlas.json"), uvMap.ToString());
            AssetDatabase.Refresh();
            Debug.Log("[IconComposer] Composed " + items.Count + " icons into " + OutputFolder +
                      " (drop overrides into " + OverrideFolder + ").");
        }

        private static void DrawIcon(Texture2D atlas, int ox, int oy, string id, string spec, int tier)
        {
            // Optional hand-drawn override (M8 top-40 pass).
            string overridePath = Path.Combine(OverrideFolder, id + ".png");
            if (File.Exists(overridePath))
            {
                var overrideTex = new Texture2D(2, 2);
                overrideTex.LoadImage(File.ReadAllBytes(overridePath));
                for (int y = 0; y < IconSize; y++)
                {
                    for (int x = 0; x < IconSize; x++)
                    {
                        atlas.SetPixel(ox + x, oy + y,
                            overrideTex.GetPixel(x * overrideTex.width / IconSize, y * overrideTex.height / IconSize));
                    }
                }
                return;
            }

            // Grammar: base color from the material segment hash, glyph from the form
            // segment, badge stripe from the verb segment (spec "material:form:verb").
            string[] parts = spec == "auto" ? new[] { id, "", "" } : spec.Split(':');
            Color baseColor = ColorFromString(parts.Length > 0 ? parts[0] : id, 0.55f, 0.7f);
            Color badge = ColorFromString(parts.Length > 2 ? parts[2] : "", 0.85f, 0.9f);
            string form = parts.Length > 1 ? parts[1] : "";

            for (int y = 0; y < IconSize; y++)
            {
                for (int x = 0; x < IconSize; x++)
                {
                    bool border = x == 0 || y == 0 || x == IconSize - 1 || y == IconSize - 1;
                    Color c = border ? Color.black : baseColor;
                    if (!border && GlyphPixel(form, x, y))
                    {
                        c = Color.Lerp(baseColor, Color.white, 0.65f);
                    }
                    if (!border && y >= IconSize - 5 && y < IconSize - 2 && x >= 2 && x < 2 + 3 + tier * 2)
                    {
                        c = badge;
                    }
                    atlas.SetPixel(ox + x, oy + y, c);
                }
            }
        }

        /// <summary>Simple 32x32 glyph per form family (readable silhouettes, docs/plan/05).</summary>
        private static bool GlyphPixel(string form, int x, int y)
        {
            int cx = x - IconSize / 2;
            int cy = y - IconSize / 2;
            switch (form)
            {
                case "ingot": return Mathf.Abs(cy) < 5 && Mathf.Abs(cx) < 9;
                case "plate": return Mathf.Abs(cy) < 8 && Mathf.Abs(cx) < 10 && Mathf.Abs(cy) > 4;
                case "rod": return Mathf.Abs(cx) < 3 && Mathf.Abs(cy) < 11;
                case "wire": return Mathf.Abs((cx + cy) % 6) < 2 && Mathf.Abs(cx) < 10 && Mathf.Abs(cy) < 10;
                case "gear": return cx * cx + cy * cy is > 25 and < 81;
                case "pipe": return cx * cx + cy * cy is > 36 and < 100 && Mathf.Abs(cy) < 6;
                case "mesh": return (x % 6 < 2 || y % 6 < 2) && Mathf.Abs(cx) < 10 && Mathf.Abs(cy) < 10;
                case "powder": return (x * 7 + y * 13) % 11 < 2 && cx * cx + cy * cy < 100;
                case "brick": return Mathf.Abs(cy) < 7 && Mathf.Abs(cx) < 9 && (y % 5 != 0) && (x % 9 != 0);
                case "gas": return cx * cx + (cy - 3) * (cy - 3) < 30 || cx * cx + (cy + 4) * (cy + 4) < 16;
                case "fluid": return cy > -8 && cy < 8 && Mathf.Abs(cx) < 7 && cy > Mathf.Abs(cx) - 8;
                case "cell": return Mathf.Abs(cx) < 5 && Mathf.Abs(cy) < 9;
                case "circuit": return (x % 8 < 2 && Mathf.Abs(cy) < 9) || (y % 8 < 2 && Mathf.Abs(cx) < 9);
                case "frame": return (Mathf.Abs(cx) is > 6 and < 10 || Mathf.Abs(cy) is > 6 and < 10) &&
                                     Mathf.Abs(cx) < 10 && Mathf.Abs(cy) < 10;
                default: return cx * cx + cy * cy < 36;
            }
        }

        private static Color ColorFromString(string key, float saturation, float value)
        {
            ulong hash = 14695981039346656037UL;
            foreach (char c in key)
            {
                hash = (hash ^ c) * 1099511628211UL;
            }
            float hue = (hash % 360UL) / 360f;
            return Color.HSVToRGB(hue, saturation, value);
        }
    }
}
