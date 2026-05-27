using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

[TestFixture]
public class ReportsControllerTests
{
    private Mock<IReportService> _reportServiceMock = null!;
    private Mock<ILogger<ReportsController>> _loggerMock = null!;
    private ReportsController _controller = null!;

    [SetUp]
    public void SetUp()
    {
        _reportServiceMock = new Mock<IReportService>();
        _loggerMock = new Mock<ILogger<ReportsController>>();

        _controller = new ReportsController(
            _reportServiceMock.Object,
            _loggerMock.Object
        );
    }

    [Test]
    public async Task DownloadFinancialReport_WhenUserIsAuthenticated_ReturnsPdfFile()
    {
        byte[] pdfBytes = { 1, 2, 3, 4 };

        _reportServiceMock
            .Setup(s => s.GenerateFinancialReportPdfAsync(1))
            .ReturnsAsync(pdfBytes);

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, "1")
                }, "TestAuth"))
            }
        };

        IActionResult result = await _controller.DownloadFinancialReport();

        result.Should().BeOfType<FileContentResult>();

        var fileResult = result as FileContentResult;
        fileResult!.ContentType.Should().Be("application/pdf");
        fileResult.FileContents.Should().BeEquivalentTo(pdfBytes);
    }

    [Test]
    public async Task DownloadFinancialReport_WhenTokenHasNoUserId_ReturnsUnauthorized()
    {
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        IActionResult result = await _controller.DownloadFinancialReport();

        result.Should().BeOfType<UnauthorizedObjectResult>();
    }
}