using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace Kirana.Application.Products;

/// <summary>
/// Turns a .csv or .xlsx file into header-keyed rows. Deliberately hand-rolled rather than taking a
/// spreadsheet dependency — Kirana ships no third-party parsers, and the same minimal OOXML handling
/// already backs the report export writer.
///
/// Header matching is forgiving (case-, space- and punctuation-insensitive) because these files are
/// usually hand-made in Excel: "Selling Price", "selling_price" and "SELLINGPRICE" all resolve to
/// the same column.
/// </summary>
public static class ProductImportParser
{
    /// <summary>Row 1 is the header, so data starts at row 2.</summary>
    public const int FirstDataRowNumber = 2;

    public sealed class ParsedFile
    {
        public IReadOnlyList<IReadOnlyDictionary<string, string>> Rows { get; init; } = [];
        public string? FatalError { get; init; }
    }

    public static ParsedFile Parse(Stream stream, string fileName)
    {
        try
        {
            var isExcel = fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase);
            var grid = isExcel ? ReadXlsx(stream) : ReadCsv(stream);

            if (grid.Count == 0)
            {
                return new ParsedFile { FatalError = "The file is empty." };
            }

            var headers = grid[0].Select(NormalizeHeader).ToList();
            if (headers.All(string.IsNullOrEmpty))
            {
                return new ParsedFile { FatalError = "The first row must be a header row naming each column." };
            }

            var rows = new List<IReadOnlyDictionary<string, string>>();
            foreach (var cells in grid.Skip(1))
            {
                // Trailing blank lines are normal in hand-edited files — skip rather than reporting
                // them as invalid rows the operator then has to hunt down and delete.
                if (cells.All(string.IsNullOrWhiteSpace))
                {
                    continue;
                }

                var row = new Dictionary<string, string>(StringComparer.Ordinal);
                for (var i = 0; i < headers.Count; i++)
                {
                    if (headers[i].Length == 0)
                    {
                        continue;
                    }

                    row[headers[i]] = i < cells.Count ? cells[i].Trim() : string.Empty;
                }

                rows.Add(row);
            }

            return new ParsedFile { Rows = rows };
        }
        catch (Exception ex)
        {
            return new ParsedFile { FatalError = $"Could not read the file: {ex.Message}" };
        }
    }

    /// <summary>Strips everything but letters and digits so header spelling variations collapse to
    /// one key. "GST %" and "gst_percent" are not the same key — callers list explicit aliases.</summary>
    public static string NormalizeHeader(string header) =>
        new(header.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    // --- CSV ---------------------------------------------------------------------------------

    private static List<List<string>> ReadCsv(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var text = reader.ReadToEnd();

        var grid = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    // "" inside a quoted field is an escaped quote, not the end of the field.
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inQuotes = true;
                    break;
                case ',':
                    row.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    row.Add(field.ToString());
                    field.Clear();
                    grid.Add(row);
                    row = [];
                    break;
                default:
                    field.Append(c);
                    break;
            }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            grid.Add(row);
        }

        return grid;
    }

    // --- XLSX --------------------------------------------------------------------------------

    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static List<List<string>> ReadXlsx(Stream stream)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var sharedStrings = ReadSharedStrings(archive);

        var sheetEntry = archive.GetEntry("xl/worksheets/sheet1.xml")
            ?? archive.Entries.FirstOrDefault(e => e.FullName.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("No worksheet found in the workbook.");

        using var sheetStream = sheetEntry.Open();
        var doc = XDocument.Load(sheetStream);

        var grid = new List<List<string>>();
        foreach (var rowElement in doc.Descendants(Main + "row"))
        {
            var cells = new List<string>();
            foreach (var cellElement in rowElement.Elements(Main + "c"))
            {
                // Cells with no value are omitted from the XML entirely, so pad using the cell
                // reference ("C5" -> column index 2) or the columns would silently shift left.
                var columnIndex = ColumnIndexFromReference((string?)cellElement.Attribute("r"));
                while (columnIndex >= 0 && cells.Count < columnIndex)
                {
                    cells.Add(string.Empty);
                }

                cells.Add(ReadCellValue(cellElement, sharedStrings));
            }

            grid.Add(cells);
        }

        return grid;
    }

    private static List<string> ReadSharedStrings(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
        {
            return [];
        }

        using var stream = entry.Open();
        var doc = XDocument.Load(stream);

        return doc.Descendants(Main + "si")
            .Select(si => string.Concat(si.Descendants(Main + "t").Select(t => t.Value)))
            .ToList();
    }

    private static string ReadCellValue(XElement cell, List<string> sharedStrings)
    {
        var type = (string?)cell.Attribute("t");

        if (type == "inlineStr")
        {
            return string.Concat(cell.Descendants(Main + "t").Select(t => t.Value));
        }

        var raw = cell.Element(Main + "v")?.Value ?? string.Empty;

        if (type == "s" && int.TryParse(raw, out var index) && index >= 0 && index < sharedStrings.Count)
        {
            return sharedStrings[index];
        }

        return raw;
    }

    /// <summary>"C5" -> 2. Returns -1 when the reference is missing or malformed, in which case the
    /// caller just appends in document order.</summary>
    private static int ColumnIndexFromReference(string? reference)
    {
        if (string.IsNullOrEmpty(reference))
        {
            return -1;
        }

        var index = 0;
        var sawLetter = false;
        foreach (var c in reference)
        {
            if (!char.IsLetter(c))
            {
                break;
            }

            sawLetter = true;
            index = (index * 26) + (char.ToUpperInvariant(c) - 'A' + 1);
        }

        return sawLetter ? index - 1 : -1;
    }
}
