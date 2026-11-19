using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace Murbingo.Core;
/// <summary>Carries a localization key and row details instead of a hardcoded user-facing parser error.</summary>
public sealed class TicketFormatException(string code, int row = 0, string detail = "") : Exception(code)
{
    public string Code { get; } = code;
    public int Row { get; } = row;
    public string Detail { get; } = detail;
}

/// <summary>Parses ticket documents without UI dependencies so the same validation can be reused outside WPF.</summary>
public static class TicketReader
{
    /// <summary>Parses delimited text with quoted cells and embedded newlines before validating complete ticket rows.</summary>
    public static IReadOnlyList<Ticket> ParseCsv(string text)
    {
        text = text.TrimStart('\uFEFF');
        var first = text.Split('\n')[0];
        char separator = first.Contains('\t') ? '\t' : first.Count(c => c == ';') > first.Count(c => c == ',') ? ';' : ',';
        var rows = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '"')
            {
                if (quoted && i + 1 < text.Length && text[i + 1] == '"')
                {
                    cell.Append('"');
                    i++;
                }
                else
                    quoted = !quoted;
            }
            else if (!quoted && c == separator)
            {
                row.Add(cell.ToString());
                cell.Clear();
            }
            else if (!quoted && (c == '\r' || c == '\n'))
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                    i++;
                row.Add(cell.ToString());
                cell.Clear();
                rows.Add(row);
                row = new();
            }
            else
                cell.Append(c);
        }

        if (quoted)
            throw new TicketFormatException("csvQuotes");
        row.Add(cell.ToString());
        rows.Add(row);
        return ParseRows(rows);
    }

    /// <summary>Reads the first worksheet directly from Open XML ZIP parts without requiring Excel or an external library.</summary>
    public static IReadOnlyList<Ticket> ParseXlsx(Stream stream)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, true);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        // Purpose: Loads a required XML package part and raises a structured format error when that part is missing.
        XDocument Read(string path)
        {
            var entry = zip.GetEntry(path) ?? throw new TicketFormatException("xlsxMissing", detail: path);
            using var s = entry.Open();
            return XDocument.Load(s);
        }

        var shared = zip.GetEntry("xl/sharedStrings.xml")is not null ? Read("xl/sharedStrings.xml").Descendants(ns + "si").Select(si => string.Concat(si.Descendants(ns + "t").Select(t => t.Value))).ToArray() : Array.Empty<string>();
        var sheetId = Read("xl/workbook.xml").Descendants(ns + "sheet").FirstOrDefault()?.Attribute(rel + "id")?.Value;
        var target = Read("xl/_rels/workbook.xml.rels").Root?.Elements().FirstOrDefault(e => (string? )e.Attribute("Id") == sheetId)?.Attribute("Target")?.Value ?? throw new TicketFormatException("xlsxMissing");
        var path = new Uri(new Uri("http://xlsx/xl/"), target).AbsolutePath.TrimStart('/');
        var rows = new List<List<string>>();
        foreach (var r in Read(path).Descendants(ns + "row"))
        {
            var cells = new List<string>();
            foreach (var c in r.Elements(ns + "c"))
            {
                string reference = (string? )c.Attribute("r") ?? "";
                int column = 0;
                foreach (char ch in reference.TakeWhile(char.IsLetter))
                    column = column * 26 + char.ToUpperInvariant(ch) - 'A' + 1;
                if (column == 0)
                    column = cells.Count + 1;
                if (column > 16384)
                    throw new TicketFormatException("xlsxMissing");
                while (cells.Count < column)
                    cells.Add("");
                string value = c.Element(ns + "v")?.Value ?? "";
                cells[column - 1] = (string? )c.Attribute("t") switch
                {
                    "s" => int.TryParse(value, out int ix) && ix >= 0 && ix < shared.Length ? shared[ix] : throw new TicketFormatException("xlsxMissing"),
                    "inlineStr" => string.Concat(c.Descendants(ns + "t").Select(t => t.Value)),
                    _ => value
                };
            }

            rows.Add(cells);
        }

        return ParseRows(rows);
    }

    /// <summary>Validates names and integer cells, records repeated values, and builds stable identities without losing unique numbers.</summary>
    static IReadOnlyList<Ticket> ParseRows(IEnumerable<List<string>> rows)
    {
        var result = new List<Ticket>();
        var occurrences = new Dictionary<string, int>();
        int rowNumber = 0;
        foreach (var row in rows)
        {
            rowNumber++;
            if (row.All(string.IsNullOrWhiteSpace))
                continue;
            string name = row[0].Trim();
            if (name.Length == 0)
                throw new TicketFormatException("emptyName", rowNumber);
            var numbers = new SortedSet<int>();
            var repeated = new SortedSet<int>();
            foreach (var cell in row.Skip(1).Where(c => !string.IsNullOrWhiteSpace(c)))
            {
                if (!decimal.TryParse(cell.Trim(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out var value) || value != decimal.Truncate(value) || value < int.MinValue || value > int.MaxValue)
                    throw new TicketFormatException("badNumber", rowNumber, cell);
                if (!numbers.Add((int)value))
                    repeated.Add((int)value);
            }

            if (numbers.Count == 0)
                throw new TicketFormatException("emptyTicket", rowNumber, name);
            string identity = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(name + "\0" + string.Join(",", numbers))));
            int occurrence = occurrences.GetValueOrDefault(identity);
            occurrences[identity] = occurrence + 1;
            result.Add(new(name, numbers.ToArray(), identity + ":" + occurrence) { RepeatedNumbers = repeated.ToArray() });
        }

        if (result.Count == 0)
            throw new TicketFormatException("noTickets");
        return result;
    }

    /// <summary>Validates a Google Sheets link and preserves its sheet identifier when constructing a public CSV export URL.</summary>
    public static Uri GoogleCsvUri(string source)
    {
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "docs.google.com")
            throw new TicketFormatException("badSource");
        var match = System.Text.RegularExpressions.Regex.Match(uri.AbsolutePath, @"^/spreadsheets/d/([a-zA-Z0-9_-]+)");
        if (!match.Success)
            throw new TicketFormatException("badSource");
        var gid = System.Text.RegularExpressions.Regex.Match(uri.Query + uri.Fragment, @"gid=(\d+)");
        return new($"https://docs.google.com/spreadsheets/d/{match.Groups[1].Value}/export?format=csv&gid={(gid.Success ? gid.Groups[1].Value : "0")}");
    }
}
