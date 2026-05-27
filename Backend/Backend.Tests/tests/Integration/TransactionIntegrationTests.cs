using System.Net;
using FluentAssertions;
using Moq;
using NUnit.Framework;

[TestFixture]
public class TransactionIntegrationTests
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
    public async Task GetTransactions_WhenAuthenticated_ReturnsOk()
    {
        _factory.TransactionServiceMock
            .Setup(s => s.GetTransactionsAsync(1))
            .ReturnsAsync(new List<TransactionDto>
            {
                new()
                {
                    Id = 1,
                    Date = new DateTime(2026, 4, 2),
                    Description = "Food order",
                    Amount = 250,
                    Category = "Food"
                }
            });

        HttpResponseMessage response = await _client.GetAsync("/api/transaction");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task GetSummary_WhenAuthenticated_ReturnsOk()
    {
        _factory.TransactionServiceMock
            .Setup(s => s.GetSummaryAsync(1))
            .ReturnsAsync(new SpendingSummaryDto
            {
                TotalSpent = 2500,
                TotalTransactions = 5,
                AverageMonthlySpend = 2500,
                HighestSpendingCategory = "Food"
            });

        HttpResponseMessage response = await _client.GetAsync("/api/transaction/summary");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task UploadFile_WhenUnsupportedFileType_ReturnsBadRequest()
    {
        using var content = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent("sample text"u8.ToArray());

        content.Add(fileContent, "file", "sample.txt");

        HttpResponseMessage response = await _client.PostAsync("/api/transaction/upload", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task UploadFile_WhenCsvIsUploaded_ReturnsOk()
    {
        _factory.TransactionServiceMock
            .Setup(s => s.ProcessAndSaveAsync(
                It.IsAny<Stream>(),
                It.IsAny<string>(),
                1))
            .ReturnsAsync(new FileProcessingResultDto
            {
                SavedCount = 2,
                DuplicateCount = 0,
                SkippedCount = 0,
                TotalRowsFound = 2,
                FileType = "CSV",
                Message = "File processed successfully."
            });

        using var content = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent(
            "Date,Description,Amount,Category\n2026-04-02,Food,250,Food"u8.ToArray());

        content.Add(fileContent, "file", "transactions.csv");

        HttpResponseMessage response = await _client.PostAsync("/api/transaction/upload", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}