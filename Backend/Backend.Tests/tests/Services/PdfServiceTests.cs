using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

[TestFixture]
public class PdfServiceTests
{
    private Mock<ILogger<PdfService>> _loggerMock = null!;
    private PdfService _pdfService = null!;

    [SetUp]
    public void SetUp()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        _loggerMock = new Mock<ILogger<PdfService>>();
        _pdfService = new PdfService(_loggerMock.Object);
    }

    private static Stream BuildPdfWithText(params string[] lines)
    {
        byte[] pdfBytes = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(30);

                page.Content().Column(column =>
                {
                    foreach (string line in lines)
                    {
                        column.Item().Text(line);
                    }
                });
            });
        }).GeneratePdf();

        return new MemoryStream(pdfBytes);
    }

    [Test]
    public async Task ParseAsync_WhenStreamIsNull_ReturnsEmptyResult()
    {
        ParsedFileResult result = await _pdfService.ParseAsync(null!, 1);

        result.Should().NotBeNull();
        result.Transactions.Should().BeEmpty();
    }

    [Test]
    public async Task ParseAsync_WhenStreamIsEmpty_ReturnsEmptyResult()
    {
        using var stream = new MemoryStream();

        ParsedFileResult result = await _pdfService.ParseAsync(stream, 1);

        result.Should().NotBeNull();
        result.Transactions.Should().BeEmpty();
    }

    [Test]
    public async Task ParseAsync_WhenPdfIsCorrupt_ReturnsEmptyResult()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("not a real pdf"));

        ParsedFileResult result = await _pdfService.ParseAsync(stream, 1);

        result.Should().NotBeNull();
        result.Transactions.Should().BeEmpty();
    }

    [Test]
    public async Task ParseAsync_WhenPdfHasGooglePayRows_ReturnsTransactions()
    {
        using Stream stream = BuildPdfWithText(
            "Date & time Transaction details Amount",
            "02 Apr, 2026 02:54 PM Paid to Jio Prepaid UPI Transaction ID: 609210 Paid by State Bank of India 2883 Rs 29",
            "03 Apr, 2026 02:08 PM Paid to VAMIL ENTERPRISES UPI Transaction ID: 6083 Paid by State Bank of India 2883 Rs 665",
            "05 Apr, 2026 05:50 PM Paid to Jayarani ramesh Monisha UPI Transaction ID: 57 Paid by State Bank of India 2883 Rs 15000"
        );

        ParsedFileResult result = await _pdfService.ParseAsync(stream, 3);

        result.Transactions.Should().HaveCountGreaterOrEqualTo(3);
        result.Transactions.Should().Contain(t => t.Amount == 29m);
        result.Transactions.Should().Contain(t => t.Amount == 665m);
        result.Transactions.Should().Contain(t => t.Amount == 15000m);
    }

    [Test]
    public async Task ParseAsync_WhenRowHasUpiId_DoesNotTakeUpiIdAsAmount()
    {
        using Stream stream = BuildPdfWithText(
            "02 Apr, 2026 02:54 PM Paid to Jio Prepaid UPI Transaction ID: 609210 Paid by State Bank of India 2883 Rs 29"
        );

        ParsedFileResult result = await _pdfService.ParseAsync(stream, 1);

        result.Transactions.Should().ContainSingle();
        result.Transactions[0].Amount.Should().Be(29m);
        result.Transactions[0].Amount.Should().NotBe(609210m);
        result.Transactions[0].Amount.Should().NotBe(2883m);
    }
}