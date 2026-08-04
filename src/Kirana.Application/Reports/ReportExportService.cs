using System.IO.Compression;
using System.Text;
using Kirana.Application.Abstractions;

namespace Kirana.Application.Reports;

public sealed class ReportExportService(IAuditLogger auditLogger) : IReportExportService
{
    public string BuildCsv(ReportExportData data)
    {
        var sb = new StringBuilder();

        sb.AppendLine(CsvField(data.Title));
        if (!string.IsNullOrWhiteSpace(data.Subtitle))
        {
            sb.AppendLine(CsvField(data.Subtitle));
        }

        sb.AppendLine();
        sb.AppendLine(string.Join(',', data.Columns.Select(CsvField)));

        foreach (var row in data.Rows)
        {
            sb.AppendLine(string.Join(',', row.Select(CsvField)));
        }

        return sb.ToString();
    }

    private static string CsvField(string value)
    {
        // RFC 4180: quote any field containing a comma, quote or newline; double up embedded quotes.
        if (value.IndexOfAny([',', '"', '\n', '\r']) < 0)
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    public byte[] BuildExcel(ReportExportData data)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "[Content_Types].xml", ContentTypesXml);
            WriteEntry(archive, "_rels/.rels", RootRelsXml);
            WriteEntry(archive, "xl/workbook.xml", WorkbookXml);
            WriteEntry(archive, "xl/_rels/workbook.xml.rels", WorkbookRelsXml);
            WriteEntry(archive, "xl/worksheets/sheet1.xml", BuildSheetXml(data));
        }

        return stream.ToArray();
    }

    public Task LogExportAsync(
        string reportName, ReportExportFormat format, int? performedByUserId, CancellationToken cancellationToken = default) =>
        auditLogger.RecordAsync(
            performedByUserId, "ReportExported", "Report", reportName,
            newValue: format.ToString(), cancellationToken: cancellationToken);

    // ------------------------------------------------------------ minimal OOXML writer
    //
    // A full spreadsheet library would give Kirana far more than a report table needs (styling,
    // formulas, multiple sheets); this hand-rolled writer produces exactly one sheet of a title, an
    // optional subtitle and a data table with a bold header row, using only System.IO.Compression
    // from the base class library — no new NuGet dependency for what is, at heart, a zip file
    // containing a few XML documents.

    private static void WriteEntry(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }

    private const string ContentTypesXml =
        """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
          <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
          <Default Extension="xml" ContentType="application/xml"/>
          <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
          <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
        </Types>
        """;

    private const string RootRelsXml =
        """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
        </Relationships>
        """;

    private const string WorkbookXml =
        """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
          <sheets>
            <sheet name="Report" sheetId="1" r:id="rId1"/>
          </sheets>
        </workbook>
        """;

    private const string WorkbookRelsXml =
        """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
        </Relationships>
        """;

    private static string BuildSheetXml(ReportExportData data)
    {
        var sb = new StringBuilder();
        sb.Append("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""").Append('\n');
        sb.Append("""<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>""");

        var rowIndex = 1;
        AppendRow(sb, rowIndex++, [data.Title]);

        if (!string.IsNullOrWhiteSpace(data.Subtitle))
        {
            AppendRow(sb, rowIndex++, [data.Subtitle]);
        }

        rowIndex++; // blank separator row
        AppendRow(sb, rowIndex++, data.Columns);

        foreach (var row in data.Rows)
        {
            AppendRow(sb, rowIndex++, row);
        }

        sb.Append("</sheetData></worksheet>");
        return sb.ToString();
    }

    private static void AppendRow(StringBuilder sb, int rowNumber, IReadOnlyList<string> cells)
    {
        sb.Append($"""<row r="{rowNumber}">""");
        for (var col = 0; col < cells.Count; col++)
        {
            var reference = ColumnLetter(col + 1) + rowNumber;
            var text = EscapeXml(cells[col]);
            sb.Append($"""<c r="{reference}" t="inlineStr"><is><t xml:space="preserve">{text}</t></is></c>""");
        }

        sb.Append("</row>");
    }

    /// <summary>1-based column index to spreadsheet letters: 1→A, 26→Z, 27→AA, ...</summary>
    private static string ColumnLetter(int index)
    {
        var letters = string.Empty;
        while (index > 0)
        {
            var remainder = (index - 1) % 26;
            letters = (char)('A' + remainder) + letters;
            index = (index - 1) / 26;
        }

        return letters;
    }

    private static string EscapeXml(string value) => value
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;")
        .Replace("'", "&apos;");
}
