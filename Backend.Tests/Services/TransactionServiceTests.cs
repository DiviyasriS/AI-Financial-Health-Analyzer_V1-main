using Moq;
using FluentAssertions;
using Xunit;

public class TransactionServiceTests
{
    private readonly Mock<ITransactionRepository> _repoMock;
    private readonly Mock<CsvService> _csvMock;
    private readonly Mock<XlsxService> _xlsxMock;
    private readonly TransactionService _sut;

    public TransactionServiceTests()
    {
        _repoMock = new Mock<ITransactionRepository>();
        _csvMock  = new Mock<CsvService>();
        _xlsxMock = new Mock<XlsxService>();
        _sut      = new TransactionService(
            _repoMock.Object, _csvMock.Object, _xlsxMock.Object);
    }

    [Fact]
    public async Task GetSummaryAsync_ReturnsEmpty_WhenNoTransactions()
    {
        _repoMock
            .Setup(r => r.GetByUserIdAsync(It.IsAny<int>()))
            .ReturnsAsync(new List<Transaction>());

        var result = await _sut.GetSummaryAsync(userId: 1);

        result.TotalSpent.Should().Be(0);
        result.TotalTransactions.Should().Be(0);
        result.CategoryBreakdown.Should().BeEmpty();
        result.MonthlyBreakdown.Should().BeEmpty();
    }

    [Fact]
    public async Task GetSummaryAsync_CalculatesTotalCorrectly()
    {
        var transactions = new List<Transaction>
        {
            new() { Id=1, Amount=100, Category="Food",      Date=new DateTime(2024,1,5),  Description="Lunch",   UserId=1 },
            new() { Id=2, Amount=200, Category="Transport", Date=new DateTime(2024,1,10), Description="Uber",    UserId=1 },
            new() { Id=3, Amount=50,  Category="Food",      Date=new DateTime(2024,2,1),  Description="Coffee",  UserId=1 }
        };

        _repoMock
            .Setup(r => r.GetByUserIdAsync(1))
            .ReturnsAsync(transactions);

        var result = await _sut.GetSummaryAsync(1);

        result.TotalSpent.Should().Be(350);
        result.TotalTransactions.Should().Be(3);
        result.CategoryBreakdown.Should().HaveCount(2);

        var food = result.CategoryBreakdown.First(c => c.Category == "Food");
        food.Total.Should().Be(150);
        food.PercentageOfTotal.Should().BeApproximately(42.86m, 0.01m);
    }

    [Fact]
    public async Task GetSummaryAsync_MonthlyBreakdown_IsSortedDescending()
    {
        var transactions = new List<Transaction>
        {
            new() { Amount=100, Category="X", Date=new DateTime(2024,1,1), Description="A", UserId=1 },
            new() { Amount=200, Category="X", Date=new DateTime(2024,3,1), Description="B", UserId=1 },
        };

        _repoMock.Setup(r => r.GetByUserIdAsync(1)).ReturnsAsync(transactions);

        var result = await _sut.GetSummaryAsync(1);

        result.MonthlyBreakdown[0].Month.Should().Be(3); // March first
        result.MonthlyBreakdown[1].Month.Should().Be(1); // January second
    }
}