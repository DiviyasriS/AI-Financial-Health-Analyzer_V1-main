using System.Net;
using FluentAssertions;
using Moq;
using NUnit.Framework;

[TestFixture]
public class ReportsIntegrationTests
{
    private CustomWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;

    [SetUp]
    public void SetUp()
    {
        _factory = new CustomWebApplicationFactory();
        _client = _factory.CreateClient();
    }

    [TearDown]
    public void TearDown()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Test]
    public async Task DownloadFinancialReport_WhenAuthenticated_ReturnsPdf()
    {
        _factory.ReportServiceMock
            .Setup(s => s.GenerateFinancialReportPdfAsync(1))
            .ReturnsAsync(new byte[] { 1, 2, 3, 4 });

        HttpResponseMessage response = await _client.GetAsync("/api/reports/financial/pdf");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
    }
}