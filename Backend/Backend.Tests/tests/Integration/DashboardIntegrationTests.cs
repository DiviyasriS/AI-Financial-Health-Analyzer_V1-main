using System.Net;
using FluentAssertions;
using Moq;
using NUnit.Framework;

[TestFixture]
public class DashboardIntegrationTests
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
    public async Task GetDashboardSummary_WhenAuthenticated_ReturnsOk()
    {
        _factory.TransactionServiceMock
            .Setup(s => s.GetSummaryAsync(1))
            .ReturnsAsync(new SpendingSummaryDto
            {
                TotalSpent = 5000,
                TotalTransactions = 10,
                AverageMonthlySpend = 5000,
                HighestSpendingCategory = "Food",
                CategoryBreakdown = new List<CategorySummaryDto>(),
                MonthlyBreakdown = new List<MonthlySummaryDto>()
            });

        HttpResponseMessage response = await _client.GetAsync("/api/dashboard/summary");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}