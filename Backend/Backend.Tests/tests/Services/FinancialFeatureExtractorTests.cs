using Backend.Models.ML;
using FluentAssertions;
using NUnit.Framework;

[TestFixture]
public class FinancialFeatureExtractorTests
{
    // ─── Empty / no data ──────────────────────────────────────────────────────

    [Test]
    public void Extract_WhenTransactionListIsNull_ReturnsEmptyFeatures()
    {
        var result = FinancialFeatureExtractor.Extract(null!);

        result.TotalSpend.Should().Be(0m);
        result.TotalTransactions.Should().Be(0);
    }

    [Test]
    public void Extract_WhenTransactionListIsEmpty_ReturnsEmptyFeatures()
    {
        var result = FinancialFeatureExtractor.Extract(new List<Transaction>());

        result.TotalSpend.Should().Be(0m);
        result.TotalTransactions.Should().Be(0);
    }

    [Test]
    public void Extract_WhenOnlyCreditsAndTransfersExist_ReturnsEmptyRiskFeatures()
    {
        var transactions = new List<Transaction>
        {
            new() { Date = new DateTime(2026, 4, 1), Amount = 30000m, IsCredit = true, Category = "Salary", Description = "Salary" },
            new() { Date = new DateTime(2026, 4, 2), Amount = 5000m, IsCredit = false, Category = "Transfer", Description = "Self transfer" }
        };

        var result = FinancialFeatureExtractor.Extract(transactions);

        result.TotalSpend.Should().Be(0m);
        result.TotalTransactions.Should().Be(0);
        result.MonthCount.Should().Be(0);
    }

    // ─── Basic feature extraction ─────────────────────────────────────────────

    [Test]
    public void Extract_FiltersOutCreditsAndTransfersFromSpendTotal()
    {
        var transactions = new List<Transaction>
        {
            new() { Date = new DateTime(2026, 4, 2), Amount = 1000m, IsCredit = false, Category = "Food", Description = "Restaurant" },
            new() { Date = new DateTime(2026, 4, 3), Amount = 500m, IsCredit = false, Category = "Groceries", Description = "Supermarket" },
            new() { Date = new DateTime(2026, 4, 4), Amount = 25000m, IsCredit = true, Category = "Salary", Description = "Salary" },
            new() { Date = new DateTime(2026, 4, 5), Amount = 10000m, IsCredit = false, Category = "Transfer", Description = "Self transfer" }
        };

        var result = FinancialFeatureExtractor.Extract(transactions);

        result.TotalSpend.Should().Be(1500m);
        result.TotalTransactions.Should().Be(2);
        result.MonthCount.Should().Be(1);
    }

    [Test]
    public void Extract_MonthlyAvgSpend_IsCorrectForSingleMonth()
    {
        var transactions = new List<Transaction>
        {
            new() { Date = new DateTime(2026, 4, 1), Amount = 1000m, IsCredit = false, Category = "Food", Description = "Lunch" },
            new() { Date = new DateTime(2026, 4, 15), Amount = 500m, IsCredit = false, Category = "Food", Description = "Dinner" }
        };

        var result = FinancialFeatureExtractor.Extract(transactions);

        result.MonthlyAvgSpend.Should().BeApproximately(1500f, 0.01f);
        result.MonthCount.Should().Be(1);
    }

    // ─── Month-over-month change ──────────────────────────────────────────────

    [Test]
    public void Extract_WhenTwoMonthsExist_ComputesMoMChangeCorrectly()
    {
        var transactions = new List<Transaction>
        {
            new() { Date = new DateTime(2026, 4, 10), Amount = 1000m, IsCredit = false, Category = "Food", Description = "Lunch" },
            new() { Date = new DateTime(2026, 5, 10), Amount = 1500m, IsCredit = false, Category = "Food", Description = "Lunch" }
        };

        var result = FinancialFeatureExtractor.Extract(transactions);

        result.MoMSpendChangePercentage.Should().BeApproximately(50f, 0.5f);  // +50%
        result.MonthCount.Should().Be(2);
    }

    [Test]
    public void Extract_WhenSpendingDecreases_MoMChangeIsNegative()
    {
        var transactions = new List<Transaction>
        {
            new() { Date = new DateTime(2026, 4, 10), Amount = 2000m, IsCredit = false, Category = "Food", Description = "A" },
            new() { Date = new DateTime(2026, 5, 10), Amount = 1000m, IsCredit = false, Category = "Food", Description = "B" }
        };

        var result = FinancialFeatureExtractor.Extract(transactions);

        result.MoMSpendChangePercentage.Should().BeApproximately(-50f, 0.5f); // -50%
    }

    [Test]
    public void Extract_WhenOnlyOneMonth_MoMChangeIsZero()
    {
        var transactions = new List<Transaction>
        {
            new() { Date = new DateTime(2026, 4, 10), Amount = 1000m, IsCredit = false, Category = "Food", Description = "Lunch" }
        };

        var result = FinancialFeatureExtractor.Extract(transactions);

        result.MoMSpendChangePercentage.Should().Be(0f);
    }

    // ─── Category percentages ─────────────────────────────────────────────────

    [Test]
    public void Extract_FoodSpendPercentage_ComputedFromFoodKeywordsInCategoryAndDescription()
    {
        var transactions = new List<Transaction>
        {
            new() { Date = new DateTime(2026, 4, 1), Amount = 800m, IsCredit = false, Category = "Food", Description = "Restaurant" },
            new() { Date = new DateTime(2026, 4, 2), Amount = 200m, IsCredit = false, Category = "Shopping", Description = "Clothes" }
        };

        var result = FinancialFeatureExtractor.Extract(transactions);

        result.FoodSpendPercentage.Should().BeApproximately(80f, 0.5f);
    }

    [Test]
    public void Extract_FoodSpendPercentage_MatchesByDescriptionKeywordEvenIfCategoryIsOther()
    {
        var transactions = new List<Transaction>
        {
            new() { Date = new DateTime(2026, 4, 1), Amount = 400m, IsCredit = false, Category = "Others", Description = "Zomato order" },
            new() { Date = new DateTime(2026, 4, 2), Amount = 600m, IsCredit = false, Category = "Shopping", Description = "Flipkart" }
        };

        var result = FinancialFeatureExtractor.Extract(transactions);

        // Zomato matches food keyword
        result.FoodSpendPercentage.Should().BeApproximately(40f, 1f);
    }

    [Test]
    public void Extract_EntertainmentSpendPercentage_MatchesNetflixAndSpotify()
    {
        var transactions = new List<Transaction>
        {
            new() { Date = new DateTime(2026, 4, 1), Amount = 500m, IsCredit = false, Category = "Others", Description = "Netflix subscription" },
            new() { Date = new DateTime(2026, 4, 2), Amount = 500m, IsCredit = false, Category = "Others", Description = "Grocery" }
        };

        var result = FinancialFeatureExtractor.Extract(transactions);

        result.EntertainmentSpendPercentage.Should().BeApproximately(50f, 1f);
    }

    // ─── Top category ─────────────────────────────────────────────────────────

    [Test]
    public void Extract_TopCategoryPercentage_ReflectsHighestSpendingCategory()
    {
        var transactions = new List<Transaction>
        {
            new() { Date = new DateTime(2026, 4, 1), Amount = 700m, IsCredit = false, Category = "Rent", Description = "Monthly rent" },
            new() { Date = new DateTime(2026, 4, 2), Amount = 200m, IsCredit = false, Category = "Food", Description = "Lunch" },
            new() { Date = new DateTime(2026, 4, 3), Amount = 100m, IsCredit = false, Category = "Transport", Description = "Uber" }
        };

        var result = FinancialFeatureExtractor.Extract(transactions);

        // Rent = 700 / 1000 = 70%
        result.TopCategory.Should().NotBeNullOrWhiteSpace();
        result.TopCategoryPercentage.Should().BeApproximately(80f, 1f);
    }

    // ─── Large transaction frequency ─────────────────────────────────────────

    [Test]
    public void Extract_LargeTransactionFrequency_CountsTransactionsAboveTwiceTheAverage()
    {
        // Average amount = (100 + 100 + 100 + 500) / 4 = 200
        // Large threshold = 2 × 200 = 400
        // Only 500 > 400, so 1 large transaction
        var transactions = new List<Transaction>
        {
            new() { Date = new DateTime(2026, 4, 1), Amount = 100m, IsCredit = false, Category = "Food", Description = "A" },
            new() { Date = new DateTime(2026, 4, 2), Amount = 100m, IsCredit = false, Category = "Food", Description = "B" },
            new() { Date = new DateTime(2026, 4, 3), Amount = 100m, IsCredit = false, Category = "Food", Description = "C" },
            new() { Date = new DateTime(2026, 4, 4), Amount = 500m, IsCredit = false, Category = "Shopping", Description = "Electronics" }
        };

        var result = FinancialFeatureExtractor.Extract(transactions);

        result.LargeTransactionFrequency.Should().BeApproximately(1f, 0.01f);
    }

    // ─── Standard deviation ───────────────────────────────────────────────────

    [Test]
    public void Extract_WhenSpendingIsIdenticalAcrossMonths_StdDevIsZero()
    {
        var transactions = new List<Transaction>
        {
            new() { Date = new DateTime(2026, 4, 1), Amount = 1000m, IsCredit = false, Category = "Food", Description = "A" },
            new() { Date = new DateTime(2026, 5, 1), Amount = 1000m, IsCredit = false, Category = "Food", Description = "B" }
        };

        var result = FinancialFeatureExtractor.Extract(transactions);

        result.MonthlySpendStdDev.Should().BeApproximately(0f, 0.01f);
    }

    [Test]
    public void Extract_WhenSpendingVariesAcrossMonths_StdDevIsPositive()
    {
        var transactions = new List<Transaction>
        {
            new() { Date = new DateTime(2026, 4, 1), Amount = 500m, IsCredit = false, Category = "Food", Description = "A" },
            new() { Date = new DateTime(2026, 5, 1), Amount = 1500m, IsCredit = false, Category = "Food", Description = "B" }
        };

        var result = FinancialFeatureExtractor.Extract(transactions);

        result.MonthlySpendStdDev.Should().BeGreaterThan(0f);
    }

    // ─── Monthly totals list ──────────────────────────────────────────────────

    [Test]
    public void Extract_MonthlyTotals_AreOrderedChronologically()
    {
        var transactions = new List<Transaction>
        {
            new() { Date = new DateTime(2026, 5, 1), Amount = 2000m, IsCredit = false, Category = "Food", Description = "A" },
            new() { Date = new DateTime(2026, 3, 1), Amount = 500m, IsCredit = false, Category = "Food", Description = "B" },
            new() { Date = new DateTime(2026, 4, 1), Amount = 1000m, IsCredit = false, Category = "Food", Description = "C" }
        };

        var result = FinancialFeatureExtractor.Extract(transactions);

        result.MonthlyTotals.Should().ContainInOrder(500m, 1000m, 2000m);
    }
}