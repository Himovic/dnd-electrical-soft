using System.Text;

namespace DND.Electrical.Reports;

/// <summary>RFC 4180 CSV writer (UTF-8 with BOM so spreadsheet applications detect the encoding).</summary>
public static class CsvWriter
{
    public static string ToCsv(ReportTable table, char separator = ',')
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(separator, table.Columns.Select(c => Escape(c.Header, separator))));
        foreach (var row in table.Rows) sb.AppendLine(string.Join(separator, row.Select(v => Escape(v, separator))));
        return sb.ToString();
    }

    public static void Write(ReportTable table, string path, char separator = ',') =>
        File.WriteAllText(path, ToCsv(table, separator), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

    public static string Escape(string value, char separator)
    {
        // Neutralise spreadsheet formula injection for values starting with =, +, -, @ (except plain numbers/tags).
        if (value.Length > 0 && value[0] is '=' or '@' && !double.TryParse(value, out _)) value = "'" + value;
        var needsQuotes = value.Contains(separator) || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
        return needsQuotes ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }

    /// <summary>Parses CSV text (quoted fields, embedded separators and newlines).</summary>
    public static List<string[]> Parse(string text, char separator = ',')
    {
        var rows = new List<string[]>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        if (text.Length > 0 && text[0] == '﻿') text = text[1..];
        for (int i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(ch);
                continue;
            }
            if (ch == '"') inQuotes = true;
            else if (ch == separator) { row.Add(field.ToString()); field.Clear(); }
            else if (ch == '\n' || ch == '\r')
            {
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(field.ToString()); field.Clear();
                if (row.Count > 1 || row[0].Length > 0) rows.Add(row.ToArray());
                row.Clear();
            }
            else field.Append(ch);
        }
        if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); rows.Add(row.ToArray()); }
        return rows;
    }
}
