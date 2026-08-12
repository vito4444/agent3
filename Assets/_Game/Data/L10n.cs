using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Starsoil.Data
{
    /// <summary>
    /// Runtime localization table (Steam-standard discipline from M1 on: no hardcoded
    /// player-facing strings; docs/plan/10). Reads data/localization.csv directly in the
    /// editor and dev builds; the Unity Localization package pipeline replaces this at M8.
    /// </summary>
    public static class L10n
    {
        private static readonly Dictionary<string, string> Zh = new Dictionary<string, string>();
        private static readonly Dictionary<string, string> En = new Dictionary<string, string>();
        private static string _language = "zh";

        public static string Language => _language;

        public static void SetLanguage(string language)
        {
            _language = language == "en" ? "en" : "zh";
        }

        public static void LoadFromCsvLines(IEnumerable<string> lines)
        {
            Zh.Clear();
            En.Clear();
            bool headerSeen = false;
            foreach (string raw in lines)
            {
                string line = raw.TrimEnd('\r');
                if (line.Length == 0 || line.StartsWith("#"))
                {
                    continue;
                }
                if (!headerSeen)
                {
                    headerSeen = true;
                    continue;
                }
                var cells = SplitCsv(line);
                if (cells.Count < 3 || cells[0].Length == 0)
                {
                    continue;
                }
                Zh[cells[0]] = cells[1];
                En[cells[0]] = cells[2];
            }
        }

        /// <summary>Loads from the repo data folder (editor/dev); logs once when missing.</summary>
        public static bool TryLoadDefault()
        {
            string path = DataFiles.RepoDataPath("localization.csv");
            if (path == null || !File.Exists(path))
            {
                Debug.LogWarning("[L10n] data/localization.csv not found; keys will show raw.");
                return false;
            }
            LoadFromCsvLines(File.ReadAllLines(path));
            return true;
        }

        public static string Tr(string key)
        {
            var table = _language == "en" ? En : Zh;
            return table.TryGetValue(key, out string value) ? value : key;
        }

        public static string TrF(string key, params object[] args)
        {
            return string.Format(Tr(key), args);
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

    /// <summary>Locates repo data files from the editor or a dev build next to the repo.</summary>
    public static class DataFiles
    {
        public static string RepoDataPath(string fileName)
        {
            string candidate = Path.Combine(Application.dataPath, "..", "data", fileName);
            return File.Exists(candidate) ? Path.GetFullPath(candidate) : null;
        }
    }
}
