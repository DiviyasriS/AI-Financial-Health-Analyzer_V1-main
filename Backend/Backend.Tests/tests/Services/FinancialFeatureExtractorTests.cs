using Backend.Models.ML;
using FluentAssertions;
using NUnit.Framework;

[TestFixture]
public class FinancialFeatureExtractorTests
{
    [Test]
    public void Extract_WhenTransactionsContainCreditsAndTransfers_UsesOnlyRealSpendingForRiskFeatures()
    {
        var transactions = new List<Transaction>
        {
            new() { Date = new DateTime(2026, 4, 2), Amount = 1000m, IsCredit = false, Category = "Food", Description = "Paid to restaurant" },
            new() { Date = new DateTime(2026, 4, 3), Amount = 500m, IsCredit = false, Category = "Groceries", Description = "Supermarket" },
            new() { Date = new DateTime(2026, 4, 4), Amount = 25000m, IsCredit = true, Category = "Salary", Description = "Salary credited" },
            new() { Date = new DateTime(2026, 4, 5), Amount = 10000m, IsCredit = false, Category = "Transfer", Description = "Self transfer to savings" }
        };

        UserRiskFeatures features = FinancialFeatureExtractor.Extract(transactions);

        features.TotalSpend.Should().Be(1500m);
        features.TotalTransactions.Should().Be(2);
        features.MonthCount.Should().Be(1);
        features.MonthlyAvgSpend.Should().Be(1500f);
        features.MonthlyTotals.Should().ContainSingle().Which.Should().Be(1500m);
    }

    [Test]
    public void Extract_WhenTwoMonthsExist_ComputesMonthOverMonthChange()
    {
        var transactions = new List<Transaction>
        {
            new() { Date = new DateTime(2026, 4, 10), Amount = 1000m, IsCredit = false, Category = "Food", Description = "Restaurant" },
            new() { Date = new DateTime(2026, 5, 10), Amount = 1500m, IsCredit = false, Category = "Food", Description = "Restaurant" }
        };

        UserRiskFeatures features = FinancialFeatureExtractor.Extract(transactions);

        features.MonthCount.Should().Be(2);
        features.MoMSpendChangePercentage.Should().Be(50f);
    }

    [Test]
    public void Extract_WhenOnlyCreditsAndTransfersExist_ReturnsEmptyRiskFeatures()
    {
        var transactions = new List<Transaction>
        {
            new() { Date = new DateTime(2026, 4, 1), Amount = 30000m, IsCredit = true, Category = "Salary", Description = "Salary credited" },
            new() { Date = new DateTime(2026, 4, 2), Amount = 5000m, IsCredit = false, Category = "Transfer", Description = "Transfer to self" }
        };

        UserRiskFeatures features = FinancialFeatureExtractor.Extract(transactions);

        features.TotalSpend.Should().Be(0m);
        features.TotalTransactions.Should().Be(0);
        features.MonthCount.Should().Be(0);
    }
}
