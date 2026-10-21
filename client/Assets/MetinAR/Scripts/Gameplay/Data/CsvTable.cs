using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MetinAR.Gameplay.Data
{
    /// <summary>Minimal RFC 4180 CSV reader for docs/design/tables/*.csv (header row, comma separated, optional quotes).</summary>
    public sealed class CsvTable
    {
        private readonly Dictionary<string, int> _columns = new Dictionary<string, int>(StringComparer.Ordinal);

        public IReadOnlyList<string> Header { get; }
        public IReadOnlyList<string[]> Rows { get; }

        private CsvTable(List<string> header, List<string[]> rows)
        {
            Header = header;
            Rows = rows;
            for (int i = 0; i < header.Count; i++)
            {
                if (_columns.ContainsKey(header[i])) throw new FormatException("Duplicate CSV column '" + header[i] + "'.");
                _columns[header[i]] = i;
            }
        }

        public static CsvTable Parse(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            if (text.Length > 0 && text[0] == '\uFEFF') text = text.Substring(1);
            var records = ReadRecords(text);
            if (records.Count == 0) throw new FormatException("CSV has no header.");
            var header = records[0];
            var rows = new List<string[]>();
            for (int i = 1; i < records.Count; i++)
            {
                var rec = records[i];
                if (rec.Count == 1 && rec[0].Length == 0) continue; // blank line
                if (rec.Count != header.Count)
                    throw new FormatException("CSV row " + (i + 1) + " has " + rec.Count + " fields, header has " + header.Count + ".");
                rows.Add(rec.ToArray());
            }
            return new CsvTable(header, rows);
        }

        public bool HasColumn(string name) { return _columns.ContainsKey(name); }

        public int Column(string name)
        {
            int index;
            if (!_columns.TryGetValue(name, out index)) throw new FormatException("CSV column '" + name + "' not found.");
            return index;
        }

        public string Get(int row, string column) { return Rows[row][Column(column)]; }

        public int GetInt(int row, string column)
        {
            string s = Get(row, column);
            int v;
            if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v))
                throw new FormatException("CSV row " + (row + 2) + " column '" + column + "': '" + s + "' is not an integer.");
            return v;
        }

        public long GetLong(int row, string column)
        {
            string s = Get(row, column);
            long v;
            if (!long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v))
                throw new FormatException("CSV row " + (row + 2) + " column '" + column + "': '" + s + "' is not an integer.");
            return v;
        }

        public double GetDouble(int row, string column)
        {
            string s = Get(row, column);
            double v;
            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                throw new FormatException("CSV row " + (row + 2) + " column '" + column + "': '" + s + "' is not a number.");
            return v;
        }

        private static List<List<string>> ReadRecords(string text)
        {
            var records = new List<List<string>>();
            var record = new List<string>();
            var field = new StringBuilder();
            bool inQuotes = false;
            bool any = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                any = true;
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else field.Append(c);
                    continue;
                }
                switch (c)
                {
                    case '"': inQuotes = true; break;
                    case ',': record.Add(field.ToString()); field.Length = 0; break;
                    case '\r': break;
                    case '\n':
                        record.Add(field.ToString()); field.Length = 0;
                        records.Add(record); record = new List<string>(); any = false;
                        break;
                    default: field.Append(c); break;
                }
            }
            if (inQuotes) throw new FormatException("Unterminated quoted CSV field.");
            if (any || field.Length > 0 || record.Count > 0) { record.Add(field.ToString()); records.Add(record); }
            return records;
        }
    }
}
