using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Writer;

/// <summary>
/// Unit tests for <see cref="PdfService"/>.
///
/// We use PdfPig's own <see cref="PdfDocumentBuilder"/> to generate minimal
/// in-memory PDFs so the tests have no external file dependencies.
/// </summary>
[TestFixture]
public class PdfServiceTests
{
    private Mock<ILogger<PdfService>> _loggerMock = null!;
    private PdfService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _loggerMock = new Mock<ILogger<PdfService>>();
        _service    = new PdfService(_loggerMock.Object);
    }

    // ────────────────────────────────────────────────────────────────────
    // Helper: build an in-memory PDF containing one page of text
    // ────────────────────────────────────────────────────────────────────

    private static Stream BuildPdfWithText(string pageContent)
    {
        var builder = new PdfDocumentBuilder();
        PdfDocumentBuilder.PdfPageBuilder page = builder.AddPage(PdfDocumentBuilder.StandardPageSizes.A4);

        // PdfPig's builder requires a font; we embed Helvetica.
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);

        // Render the lines top-to-bottom (each line offset by ~20 pts)
        double y = 800;
        foreach (string line in pageContent.Split('\n'))
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                page.AddText(line.Trim(), 10, new UglyToad.PdfPig.Core.PdfPoint(40, y), font);
                y -= 20;
            }
        }

        byte[] bytes = builder.Build();
        return new MemoryStream(bytes);
    }

    // ────────────────────────────────────────────────────────────────────
    // 1. Valid PDF — standard tabular format
    // ────────────────────────────────────────────────────────────────────

    [Test]
    public async Task ParseAsync_ValidPdf_ReturnsParsedTransactions()
    {
        // Arrange: two valid transaction rows separated by 2+ spaces
        string content =
            "Date          Description              Amount    Category\n" +
            "01/04/2025    Amazon Shopping          1250.00   Shopping\n" +
            "03/04/2025    Salary Credit            50000.00  Income\n";

        using Stream stream = BuildPdfWithText(content);

        // Act
        ParsedFileResult result = await _service.ParseAsync(stream, userId: 1);

        // Assert
        result.Should().NotBeNull();
        result.Transactions.Should().HaveCount(2);

        Transaction first = result.Transactions[0];
        first.Date.Should().Be(new DateTime(2025, 4, 1, 0, 0, 0, DateTimeKind.Utc));
        first.Amount.Should().Be(1250.00m);
        first.UserId.Should().Be(1);

        Transaction second = result.Transactions[1];
        second.Date.Should().Be(new DateTime(2025, 4, 3, 0, 0, 0, DateTimeKind.Utc));
        second.Amount.Should().Be(50000.00m);
    }

    // ────────────────────────────────────────────────────────────────────
    // 2. Empty stream → returns empty result, no exception
    // ────────────────────────────────────────────────────────────────────

    [Test]
    public async Task ParseAsync_EmptyStream_ReturnsEmptyResult()
    {
        // Arrange
        using Stream emptyStream = new MemoryStream(Array.Empty<byte>());

        // Act
        ParsedFileResult result = await _service.ParseAsync(emptyStream, userId: 1);

        // Assert
        result.Transactions.Should().BeEmpty();
        result.TotalRowsFound.Should().Be(0);
    }

    // ────────────────────────────────────────────────────────────────────
    // 3. Invalid / corrupt bytes → returns empty result gracefully
    // ────────────────────────────────────────────────────────────────────

    [Test]
    public async Task ParseAsync_CorruptBytes_ReturnsEmptyResultWithoutThrowing()
    {
        // Arrange: random bytes that are not a valid PDF
        byte[] garbage = Encoding.UTF8.GetBytes("THIS IS NOT A PDF");
        using Stream stream = new MemoryStream(garbage);

        // Act & Assert: must not throw
        ParsedFileResult result = await _service.ParseAsync(stream, userId: 1);
        result.Transactions.Should().BeEmpty();
    }

    // ────────────────────────────────────────────────────────────────────
    // 4. PDF with only header rows → no transactions extracted
    // ────────────────────────────────────────────────────────────────────

    [Test]
    public async Task ParseAsync_HeaderOnlyPdf_ReturnsEmptyTransactions()
    {
        // Arrange: only header lines, no data
        string content =
            "Date          Description              Amount    Category\n" +
            "Statement for April 2025\n" +
            "Page 1 of 1\n";

        using Stream stream = BuildPdfWithText(content);

        // Act
        ParsedFileResult result = await _service.ParseAsync(stream, userId: 1);

        // Assert
        result.Transactions.Should().BeEmpty();
    }

    // ────────────────────────────────────────────────────────────────────
    // 5. Malformed transaction rows → skipped, others still parsed
    // ────────────────────────────────────────────────────────────────────

    [Test]
    public async Task ParseAsync_MalformedRows_SkipsInvalidAndParsesValid()
    {
        // Arrange: first row is valid; second has no parseable amount; third is valid
        string content =
            "01/04/2025    Amazon                   1250.00   Shopping\n" +
            "INVALID ROW WITH NO DATE OR AMOUNT WHATSOEVER\n" +
            "05/04/2025    Utility Bill             900.00    Utilities\n";

        using Stream stream = BuildPdfWithText(content);

        // Act
        ParsedFileResult result = await _service.ParseAsync(stream, userId: 42);

        // Assert: at least the two valid rows are parsed; malformed is skipped
        result.Transactions.Should().HaveCountGreaterOrEqualTo(2);
        result.Transactions.Should().Contain(t => t.Amount == 1250.00m);
        result.Transactions.Should().Contain(t => t.Amount == 900.00m);
    }

    // ────────────────────────────────────────────────────────────────────
    // 6. Amount parsing — European format (1.234,56)
    // ────────────────────────────────────────────────────────────────────

    [Test]
    public async Task ParseAsync_EuropeanAmountFormat_ParsedCorrectly()
    {
        // Arrange: amount uses European decimal comma
        string content =
            "01/04/2025    Coffee Shop              1.234,56   Food\n";

        using Stream stream = BuildPdfWithText(content);

        // Act
        ParsedFileResult result = await _service.ParseAsync(stream, userId: 1);

        // Assert
        result.Transactions.Should().ContainSingle();
        result.Transactions[0].Amount.Should().Be(1234.56m);
    }

    // ────────────────────────────────────────────────────────────────────
    // 7. Amount parsing — parenthesised debit notation
    // ────────────────────────────────────────────────────────────────────

    [Test]
    public async Task ParseAsync_ParenthesisedDebitAmount_ParsedAsPositive()
    {
        // Arrange: bank statement shows debits as (500.00)
        string content =
            "15/04/2025    ATM Withdrawal           (500.00)   Cash\n";

        using Stream stream = BuildPdfWithText(content);

        // Act
        ParsedFileResult result = await _service.ParseAsync(stream, userId: 1);

        // Assert: stored as positive absolute value
        result.Transactions.Should().ContainSingle();
        result.Transactions[0].Amount.Should().Be(500.00m);
    }

    // ────────────────────────────────────────────────────────────────────
    // 8. Zero-amount rows are skipped
    // ────────────────────────────────────────────────────────────────────

    [Test]
    public async Task ParseAsync_ZeroAmountRows_AreSkipped()
    {
        // Arrange
        string content =
            "01/04/2025    Pending Transaction      0.00       Pending\n" +
            "02/04/2025    Real Expense             300.00     Food\n";

        using Stream stream = BuildPdfWithText(content);

        // Act
        ParsedFileResult result = await _service.ParseAsync(stream, userId: 1);

        // Assert: zero-amount row skipped
        result.Transactions.Should().ContainSingle();
        result.Transactions[0].Amount.Should().Be(300.00m);
    }

    // ────────────────────────────────────────────────────────────────────
    // 9. UserId is correctly stamped on every parsed transaction
    // ────────────────────────────────────────────────────────────────────

    [Test]
    public async Task ParseAsync_ValidPdf_StampsCorrectUserId()
    {
        // Arrange
        string content =
            "10/04/2025    Netflix Subscription     649.00    Entertainment\n";

        using Stream stream = BuildPdfWithText(content);

        // Act
        ParsedFileResult result = await _service.ParseAsync(stream, userId: 99);

        // Assert
        result.Transactions.Should().NotBeEmpty();
        result.Transactions.Should().OnlyContain(t => t.UserId == 99);
    }

    // ────────────────────────────────────────────────────────────────────
    // 10. Multiple pages are all parsed
    // ────────────────────────────────────────────────────────────────────

    [Test]
    public async Task ParseAsync_MultiPagePdf_ParsesAllPages()
    {
        // Arrange: build a two-page PDF
        var builder = new PdfDocumentBuilder();
        var font    = builder.AddStandard14Font(Standard14Font.Helvetica);

        void AddPage(string line)
        {
            var p = builder.AddPage(PdfDocumentBuilder.StandardPageSizes.A4);
            p.AddText(line, 10, new UglyToad.PdfPig.Core.PdfPoint(40, 700), font);
        }

        AddPage("01/04/2025    Amazon               1200.00   Shopping");
        AddPage("15/04/2025    Salary               50000.00  Income");

        using Stream stream = new MemoryStream(builder.Build());

        // Act
        ParsedFileResult result = await _service.ParseAsync(stream, userId: 1);

        // Assert: both pages contribute
        result.Transactions.Should().HaveCount(2);
    }

    // ────────────────────────────────────────────────────────────────────
    // 11. Null stream → returns empty result, logs warning, does not throw
    // ────────────────────────────────────────────────────────────────────

    [Test]
    public async Task ParseAsync_NullStream_ReturnsEmptyResultSafely()
    {
        // Act & Assert
        ParsedFileResult result = await _service.ParseAsync(null!, userId: 1);
        result.Transactions.Should().BeEmpty();
    }

    // ────────────────────────────────────────────────────────────────────
    // 12. Date stored as UTC Kind
    // ────────────────────────────────────────────────────────────────────

    [Test]
    public async Task ParseAsync_ValidPdf_DateKindIsUtc()
    {
        // Arrange
        string content =
            "20/04/2025    Rent Payment             15000.00   Housing\n";

        using Stream stream = BuildPdfWithText(content);

        // Act
        ParsedFileResult result = await _service.ParseAsync(stream, userId: 1);

        // Assert
        result.Transactions.Should().NotBeEmpty();
        result.Transactions[0].Date.Kind.Should().Be(DateTimeKind.Utc);
    }
}
