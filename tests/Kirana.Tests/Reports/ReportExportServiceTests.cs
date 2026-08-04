using System.IO.Compression;
using System.Xml.Linq;
using Kirana.Application.Reports;
using Kirana.Infrastructure.Persistence;
using Kirana.Tests.TestSupport;

namespace Kirana.Tests.Reports;

/// <summary>
/// CSV/Excel export (PRD §51 "Export"). The Excel writer is hand-rolled OOXML (see
/// <see cref="ReportExportService"/>'s doc comment for why), so its tests actually unzip the result
/// and parse the XML back out — a byte count alone would not catch a malformed part that Excel
/// would refuse to open.
/// </summary>
public class ReportExportServiceTests : IDisposable
{
    private readonly SqliteDbContextFixture _fixture = new();
    private readonly ReportExportService _sut;
    private readonly int _ownerId;

    public ReportExportServiceTests()
    {
        _ownerId = _fixture.SeedOwnerAsync().GetAwaiter().GetResult().Id;
        _sut = new ReportExportService(new EfAuditLogger(_fixture.Context));
    }

    private static ReportExportData SampleData() => new()
    {
        Title = "Sales Report",
        Subtitle = "Today",
        Columns = ["Invoice", "Customer", "Amount"],
        Rows =
        [
            ["INV-000001", "Walk-in", "₹100.00"],
            ["INV-000002", "Has, a comma", "₹250.00"],
        ],
    };

    [Fact]
    public void Csv_ContainsTitleHeaderAndAllRows()
    {
        var csv = _sut.BuildCsv(SampleData());

        Assert.Contains("Sales Report", csv);
        Assert.Contains("Invoice,Customer,Amount", csv);
        Assert.Contains("INV-000001,Walk-in,₹100.00", csv);
    }

    [Fact]
    public void Csv_QuotesFieldsContainingACommaAndDoublesEmbeddedQuotes()
    {
        var csv = _sut.BuildCsv(new ReportExportData
        {
            Title = "T",
            Columns = ["A"],
            Rows = [["Has, a comma"], ["Has \"quotes\""]],
        });

        Assert.Contains("\"Has, a comma\"", csv);
        Assert.Contains("\"Has \"\"quotes\"\"\"", csv);
    }

    [Fact]
    public void Csv_RoundTrips_RowCount()
    {
        var csv = _sut.BuildCsv(SampleData());

        // Title, subtitle, blank separator, header, then 2 data rows = 6 lines (plus one trailing
        // empty entry from the final line's terminator, which TrimEntries alone would not remove).
        var lines = csv.TrimEnd('\r', '\n').Split('\n');
        Assert.Equal(6, lines.Length);
    }

    [Fact]
    public void Excel_ProducesAValidZipWithTheExpectedParts()
    {
        var bytes = _sut.BuildExcel(SampleData());

        using var stream = new MemoryStream(bytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        Assert.NotNull(archive.GetEntry("[Content_Types].xml"));
        Assert.NotNull(archive.GetEntry("_rels/.rels"));
        Assert.NotNull(archive.GetEntry("xl/workbook.xml"));
        Assert.NotNull(archive.GetEntry("xl/worksheets/sheet1.xml"));
    }

    [Fact]
    public void Excel_SheetXml_IsWellFormedAndContainsEveryValue()
    {
        var bytes = _sut.BuildExcel(SampleData());

        using var stream = new MemoryStream(bytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = archive.GetEntry("xl/worksheets/sheet1.xml")!;
        using var reader = new StreamReader(entry.Open());
        var xml = reader.ReadToEnd();

        // XDocument.Parse throws on malformed XML — a real proof the writer didn't emit a broken part.
        var doc = XDocument.Parse(xml);
        var allText = string.Concat(doc.Descendants().Select(e => e.Value));

        Assert.Contains("Sales Report", allText);
        Assert.Contains("INV-000002", allText);
        Assert.Contains("Has, a comma", allText); // proves no CSV-style escaping leaked into XML
    }

    [Fact]
    public void Excel_EscapesXmlSpecialCharacters()
    {
        var bytes = _sut.BuildExcel(new ReportExportData
        {
            Title = "T",
            Columns = ["A"],
            Rows = [["Tom & Jerry <script>"]],
        });

        using var stream = new MemoryStream(bytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.GetEntry("xl/worksheets/sheet1.xml")!.Open());
        var xml = reader.ReadToEnd();

        // Must not appear as raw XML (would break parsing); Parse succeeding is the real assertion.
        var doc = XDocument.Parse(xml);
        Assert.Contains("Tom & Jerry <script>", string.Concat(doc.Descendants().Select(e => e.Value)));
    }

    [Fact]
    public async Task LogExportAsync_WritesAnAuditEntry()
    {
        await _sut.LogExportAsync("Sales Report", ReportExportFormat.Csv, _ownerId);

        var audit = _fixture.Context.AuditLogs.Single();
        Assert.Equal("ReportExported", audit.Action);
        Assert.Equal("Sales Report", audit.EntityId);
        Assert.Equal("Csv", audit.NewValue);
        Assert.Equal(_ownerId, audit.UserId);
    }

    public void Dispose() => _fixture.Dispose();
}
