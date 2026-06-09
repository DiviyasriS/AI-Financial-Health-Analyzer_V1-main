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


}