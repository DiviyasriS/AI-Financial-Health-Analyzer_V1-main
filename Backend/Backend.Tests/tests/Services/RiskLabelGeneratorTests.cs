using Backend.Models.ML;
using FluentAssertions;
using NUnit.Framework;

[TestFixture]
public class RiskLabelGeneratorTests
{
    [Test]
    public void GenerateAssessment_WhenNoSpendingData_ReturnsUnknown()
    {
        RiskAssessmentResult result = RiskLabelGenerator.GenerateAssessment(new UserRiskFeatures());

        result.RiskLevel.Should().Be("Unknown");
        result.RiskScorePercent.Should().Be(0f);
        result.RiskFactors.Should().Contain(f => f.Contains("Upload debit transactions"));
    }

    [Test]
    public void GenerateAssessment_WhenStableLowSpend_ReturnsLowRiskWithPositiveSignals()
    {
        var features = new UserRiskFeatures
        {
            MonthlyAvgSpend = 8000f,
            MonthlySpendStdDev = 500f,
            TransactionFrequency = 8f,
            LargeTransactionFrequency = 1f,
            TopCategoryPercentage = 40f,
            CategoryCount = 4f,
            FoodSpendPercentage = 15f,
            EntertainmentSpendPercentage = 5f,
            TotalSpend = 16000m,
            TotalTransactions = 16,
            MonthCount = 2,
            TopCategory = "Food"
        };

        RiskAssessmentResult result = RiskLabelGenerator.GenerateAssessment(features);

        result.RiskLevel.Should().Be("Low");
        result.RiskScorePercent.Should().BeInRange(0f, 34.99f);
        result.PositiveSignals.Should().NotBeEmpty();
    }

    [Test]
    public void GenerateAssessment_WhenMultipleRiskPatternsExist_ReturnsHighRisk()
    {
        var features = new UserRiskFeatures
        {
            MonthlyAvgSpend = 90000f,
            MonthlySpendStdDev = 70000f,
            TransactionFrequency = 35f,
            LargeTransactionFrequency = 8f,
            TopCategoryPercentage = 78f,
            CategoryCount = 1f,
            FoodSpendPercentage = 45f,
            EntertainmentSpendPercentage = 35f,
            MoMSpendChangePercentage = 65f,
            SpendingTrend = 0.5f,
            TotalSpend = 180000m,
            TotalTransactions = 70,
            MonthCount = 3,
            TopCategory = "Entertainment"
        };

        RiskAssessmentResult result = RiskLabelGenerator.GenerateAssessment(features);

        result.RiskLevel.Should().Be("High");
        result.RiskScorePercent.Should().BeInRange(70f, 100f);
        result.RiskFactors.Should().HaveCountGreaterThan(3);
    }
}
