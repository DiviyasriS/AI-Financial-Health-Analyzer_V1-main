using Backend.Models.ML;
using FluentAssertions;
using NUnit.Framework;

[TestFixture]
public class RiskLabelGeneratorTests
{
    // ─── No data ──────────────────────────────────────────────────────────────

    [Test]
    public void GenerateAssessment_WhenNoTransactions_ReturnsUnknownWithZeroScore()
    {
        var result = RiskLabelGenerator.GenerateAssessment(new UserRiskFeatures
        {
            TotalTransactions = 0,
            TotalSpend = 0
        });

        result.RiskLevel.Should().Be("Unknown");
        result.RiskScorePercent.Should().Be(0f);
    }

    [Test]
    public void GenerateAssessment_WhenNoSpendingData_RiskFactorsContainsActionableMessage()
    {
        var result = RiskLabelGenerator.GenerateAssessment(new UserRiskFeatures());

        result.RiskFactors.Should().Contain(f => f.Contains("Upload debit transactions"));
    }

    // ─── Low risk ─────────────────────────────────────────────────────────────

    [Test]
    public void GenerateAssessment_WhenAllFeaturesAreLow_ReturnsLowRiskBelow35Percent()
    {
        var features = new UserRiskFeatures
        {
            MonthlyAvgSpend = 8000f,
            MonthlySpendStdDev = 500f,     // CV = 0.0625 — very stable
            TransactionFrequency = 8f,
            LargeTransactionFrequency = 0f,
            TopCategoryPercentage = 40f,
            CategoryCount = 4,
            FoodSpendPercentage = 15f,      // well under 25%
            EntertainmentSpendPercentage = 5f, // well under 15%
            MoMSpendChangePercentage = 5f,
            TotalSpend = 16000m,
            TotalTransactions = 16,
            MonthCount = 2,
            TopCategory = "Food"
        };

        var result = RiskLabelGenerator.GenerateAssessment(features);

        result.RiskLevel.Should().Be("Low");
        result.RiskScorePercent.Should().BeLessThan(35f);
        result.PositiveSignals.Should().NotBeEmpty();
    }

    [Test]
    public void GenerateLabel_WhenLowRisk_ReturnsLabelLow()
    {
        var features = new UserRiskFeatures
        {
            MonthlyAvgSpend = 5000f,
            TopCategoryPercentage = 30f,
            CategoryCount = 4,
            FoodSpendPercentage = 10f,
            EntertainmentSpendPercentage = 5f,
            TotalSpend = 5000m,
            TotalTransactions = 10,
            MonthCount = 1
        };

        var label = RiskLabelGenerator.GenerateLabel(features);

        label.Should().Be(RiskLabelGenerator.LabelLow);
    }

    // ─── Medium risk ──────────────────────────────────────────────────────────

    [Test]
    public void GenerateAssessment_WhenModerateFeaturesExist_ReturnsMediumRiskBetween35And70()
    {
        var features = new UserRiskFeatures
        {
            MonthlyAvgSpend = 35000f,       // moderately high → +2
            MonthlySpendStdDev = 12000f,    // CV = 0.34 → +1 volatility
            TransactionFrequency = 12f,
            LargeTransactionFrequency = 2f,
            TopCategoryPercentage = 55f,    // over 50 → +1
            CategoryCount = 3,
            FoodSpendPercentage = 26f,      // slightly above 25% → +1
            EntertainmentSpendPercentage = 10f,
            MoMSpendChangePercentage = 10f,
            TotalSpend = 70000m,
            TotalTransactions = 24,
            MonthCount = 2,
            TopCategory = "Shopping"
        };

        var result = RiskLabelGenerator.GenerateAssessment(features);

        result.RiskLevel.Should().Be("Medium");
        result.RiskScorePercent.Should().BeInRange(35f, 69.99f);
    }

    [Test]
    public void GenerateLabel_WhenMediumRisk_ReturnsLabelMedium()
    {
        var features = new UserRiskFeatures
        {
            MonthlyAvgSpend = 35000f,
            TopCategoryPercentage = 55f,
            CategoryCount = 2,
            FoodSpendPercentage = 28f,
            TotalSpend = 70000m,
            TotalTransactions = 20,
            MonthCount = 2
        };

        var label = RiskLabelGenerator.GenerateLabel(features);

        label.Should().Be(RiskLabelGenerator.LabelMedium);
    }

    // ─── High risk ────────────────────────────────────────────────────────────

    [Test]
    public void GenerateAssessment_WhenMultipleHighRiskFactors_ReturnsHighRiskAbove70Percent()
    {
        var features = new UserRiskFeatures
        {
            MonthlyAvgSpend = 90000f,           // > 60K → +3
            MonthlySpendStdDev = 70000f,        // CV > 0.6 → +2
            TransactionFrequency = 35f,
            LargeTransactionFrequency = 8f,     // > 5 → +2
            TopCategoryPercentage = 78f,        // > 70 → +2
            CategoryCount = 1,                  // ≤ 2 with > 5 txns → +1
            FoodSpendPercentage = 45f,          // > 40 → +2
            EntertainmentSpendPercentage = 35f, // > 30 → +2
            MoMSpendChangePercentage = 65f,     // > 40 → +2
            TotalSpend = 180000m,
            TotalTransactions = 70,
            MonthCount = 3,
            TopCategory = "Entertainment"
        };

        var result = RiskLabelGenerator.GenerateAssessment(features);

        result.RiskLevel.Should().Be("High");
        result.RiskScorePercent.Should().BeInRange(70f, 100f);
        result.RiskFactors.Should().HaveCountGreaterThan(3);
    }

    [Test]
    public void GenerateLabel_WhenHighRisk_ReturnsLabelHigh()
    {
        var features = new UserRiskFeatures
        {
            MonthlyAvgSpend = 90000f,
            FoodSpendPercentage = 50f,
            EntertainmentSpendPercentage = 40f,
            TopCategoryPercentage = 80f,
            CategoryCount = 1,
            TotalSpend = 90000m,
            TotalTransactions = 30,
            MonthCount = 1
        };

        var label = RiskLabelGenerator.GenerateLabel(features);

        label.Should().Be(RiskLabelGenerator.LabelHigh);
    }

    // ─── Individual risk factors ──────────────────────────────────────────────

    [Test]
    public void GenerateAssessment_WhenHighFoodSpend_RiskFactorsContainsFoodMessage()
    {
        var features = new UserRiskFeatures
        {
            MonthlyAvgSpend = 10000f,
            FoodSpendPercentage = 45f, // > 40%
            TopCategoryPercentage = 45f,
            CategoryCount = 3,
            TotalSpend = 10000m,
            TotalTransactions = 10,
            MonthCount = 1,
            TopCategory = "Food"
        };

        var result = RiskLabelGenerator.GenerateAssessment(features);

        result.RiskFactors.Should().Contain(f => f.Contains("Food") || f.Contains("food"));
    }

    [Test]
    public void GenerateAssessment_WhenHighVolatility_RiskFactorsContainsVolatilityMessage()
    {
        var features = new UserRiskFeatures
        {
            MonthlyAvgSpend = 10000f,
            MonthlySpendStdDev = 8000f,  // CV = 0.8 → highly volatile
            FoodSpendPercentage = 10f,
            TopCategoryPercentage = 30f,
            CategoryCount = 3,
            TotalSpend = 20000m,
            TotalTransactions = 10,
            MonthCount = 2,
            TopCategory = "Shopping"
        };

        var result = RiskLabelGenerator.GenerateAssessment(features);

        result.RiskFactors.Should().Contain(f => f.Contains("volatile") || f.Contains("volatil"));
    }

    [Test]
    public void GenerateAssessment_WhenSpendingDecreased_PositiveSignalsContainReductionMessage()
    {
        var features = new UserRiskFeatures
        {
            MonthlyAvgSpend = 8000f,
            MonthlySpendStdDev = 200f,
            FoodSpendPercentage = 10f,
            EntertainmentSpendPercentage = 5f,
            TopCategoryPercentage = 30f,
            CategoryCount = 4,
            MoMSpendChangePercentage = -30f, // spending reduced
            TotalSpend = 16000m,
            TotalTransactions = 15,
            MonthCount = 2,
            TopCategory = "Groceries"
        };

        var result = RiskLabelGenerator.GenerateAssessment(features);

        result.PositiveSignals.Should().Contain(s => s.Contains("reduc") || s.Contains("decreas") || s.Contains("30.0%"));
    }

    [Test]
    public void GenerateAssessment_WhenOnlyOneMonthData_RiskFactorsContainsLimitedDataMessage()
    {
        var features = new UserRiskFeatures
        {
            MonthlyAvgSpend = 5000f,
            TopCategoryPercentage = 30f,
            CategoryCount = 3,
            TotalSpend = 5000m,
            TotalTransactions = 10,
            MonthCount = 1,
            TopCategory = "Food"
        };

        var result = RiskLabelGenerator.GenerateAssessment(features);

        result.RiskFactors.Should().Contain(f => f.Contains("one month") || f.Contains("One month") || f.Contains("limited"));
    }

    // ─── Score clamping ───────────────────────────────────────────────────────

    [Test]
    public void GenerateAssessment_ScoreIsAlwaysBetween0And100()
    {
        // Worst possible case
        var worst = new UserRiskFeatures
        {
            MonthlyAvgSpend = 500000f,
            MonthlySpendStdDev = 400000f,
            LargeTransactionFrequency = 20f,
            TopCategoryPercentage = 95f,
            CategoryCount = 1,
            FoodSpendPercentage = 60f,
            EntertainmentSpendPercentage = 50f,
            MoMSpendChangePercentage = 100f,
            SpendingTrend = 1.0f,
            TotalSpend = 500000m,
            TotalTransactions = 100,
            MonthCount = 3,
            TopCategory = "Entertainment"
        };

        var result = RiskLabelGenerator.GenerateAssessment(worst);

        result.RiskScorePercent.Should().BeInRange(0f, 100f);
    }
}