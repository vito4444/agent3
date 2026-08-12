using System;
using System.Collections.Generic;
using System.IO;

namespace Starsoil.RecipeGen
{
    /// <summary>
    /// Minimal CSV table: first non-comment line is the header; '#' lines and blanks are
    /// skipped; fields may be double-quoted; multi-value cells use ';' between entries.
    /// </summary>
    public sealed class CsvTable
    {
        public string[] Header { get; private set; } = Array.Empty<string>();
        public List<string[]> Rows { get; } = new List<string[]>();

        public static CsvTable Load(string path)
        {
            var table = new CsvTable();
            if (!File.Exists(path))
            {
                return table;
            }
            foreach (string rawLine in File.ReadAllLines(path))
            {
                string line = rawLine.TrimEnd('\r');
                if (line.Length == 0 || line.TrimStart().StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }
                var fields = ParseLine(line);
                if (table.Header.Length == 0)
                {
                    table.Header = fields;
                }
                else
                {
                    table.Rows.Add(fields);
                }
            }
            return table;
        }

        public int Column(string name)
        {
            for (int i = 0; i < Header.Length; i++)
            {
                if (string.Equals(Header[i].Trim(), name, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
            return -1;
        }

        public string Get(string[] row, string column)
        {
            int idx = Column(column);
            return idx >= 0 && idx < row.Length ? row[idx].Trim() : string.Empty;
        }

        private static string[] ParseLine(string line)
        {
            var fields = new List<string>();
            var current = new System.Text.StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        bool escapedQuote = i + 1 < line.Length && line[i + 1] == '"';
                        if (escapedQuote)
                        {
                            current.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        current.Append(c);
                    }
                }
                else if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == ',')
                {
                    fields.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
            fields.Add(current.ToString());
            return fields.ToArray();
        }
    }
}
