using Backend.Models.ML;

/// <summary>
/// Generates an explainable financial risk assessment from a user's spending features.
///
/// IMPORTANT DOMAIN RULE:
/// RiskScore is a financial risk-severity score, not ML confidence.
/// 0% means very low observed spending risk; 100% means very high observed spending risk.
/// This prevents the UI from showing a misleading 90% score for a confident "Low" prediction.
/// </summary>
public static class RiskLabelGenerator
{
    public const float LabelLow = 0f;
    public const float LabelMedium = 1f;
    public const float LabelHigh = 2f;

    private const float MaxRiskPoints = 12f;

    public static float GenerateLabel(UserRiskFeatures features)
    {
        RiskAssessmentResult assessment = GenerateAssessment(features);

        return assessment.RiskLevel switch
        {
            "High" => LabelHigh,
            "Medium" => LabelMedium,
            _ => LabelLow
        };
    }

    public static RiskAssessmentResult GenerateAssessment(UserRiskFeatures features)
    {
        if (features.TotalTransactions <= 0 || features.TotalSpend <= 0)
        {
            return new RiskAssessmentResult
            {
                RiskLevel = "Unknown",
                RiskScorePercent = 0f,
                RawRiskPoints = 0f,
                MaxRiskPoints = MaxRiskPoints,
                Summary = "Not enough spending data is available to calculate financial risk.",
                PositiveSignals = new List<string>(),
                RiskFactors = new List<string> { "Upload debit transactions to generate a meaningful risk score." }
            };
        }

        float score = 0f;
        List<string> riskFactors = new();
        List<string> positiveSignals = new();

        AddSpendingMagnitudeRisk(features, ref score, riskFactors, positiveSignals);
        AddVolatilityRisk(features, ref score, riskFactors, positiveSignals);
        AddCategoryConcentrationRisk(features, ref score, riskFactors, positiveSignals);
        AddDiscretionarySpendRisk(features, ref score, riskFactors, positiveSignals);
        AddLargeTransactionRisk(features, ref score, riskFactors, positiveSignals);
        AddTrendRisk(features, ref score, riskFactors, positiveSignals);
        AddDataQualitySignals(features, riskFactors, positiveSignals);

        float safeScore = Math.Clamp(score, 0f, MaxRiskPoints);
        float riskScorePercent = (safeScore / MaxRiskPoints) * 100f;
        string level = riskScorePercent switch
        {
            >= 70f => "High",
            >= 35f => "Medium",
            _ => "Low"
        };

        string summary = level switch
        {
            "High" => "Your spending behavior shows multiple high-risk patterns that need attention.",
            "Medium" => "Your spending behavior is mostly manageable, but a few patterns need review.",
            "Low" => "Your spending behavior looks stable based on the available transaction history.",
            _ => "Risk level could not be determined."
        };

        return new RiskAssessmentResult
        {
            RiskLevel = level,
            RiskScorePercent = riskScorePercent,
            RawRiskPoints = safeScore,
            MaxRiskPoints = MaxRiskPoints,
            Summary = summary,
            RiskFactors = riskFactors,
            PositiveSignals = positiveSignals
        };
    }

    private static void AddSpendingMagnitudeRisk(
        UserRiskFeatures features,
        ref float score,
        List<string> riskFactors,
        List<string> positiveSignals)
    {
        if (features.MonthlyAvgSpend > 60_000f)
        {
            score += 3f;
            riskFactors.Add($"Monthly average spend is high at ₹{features.MonthlyAvgSpend:F0}.");
        }
        else if (features.MonthlyAvgSpend > 30_000f)
        {
            score += 2f;
            riskFactors.Add($"Monthly average spend is moderately high at ₹{features.MonthlyAvgSpend:F0}.");
        }
        else if (features.MonthlyAvgSpend > 15_000f)
        {
            score += 1f;
            riskFactors.Add($"Monthly average spend is rising at ₹{features.MonthlyAvgSpend:F0}.");
        }
        else
        {
            positiveSignals.Add($"Monthly average spend is controlled at ₹{features.MonthlyAvgSpend:F0}.");
        }
    }

    private static void AddVolatilityRisk(
        UserRiskFeatures features,
        ref float score,
        List<string> riskFactors,
        List<string> positiveSignals)
    {
        if (features.MonthCount < 2 || features.MonthlyAvgSpend <= 0)
        {
            return;
        }

        float coefficientOfVariation = features.MonthlySpendStdDev / features.MonthlyAvgSpend;

        if (coefficientOfVariation > 0.6f)
        {
            score += 2f;
            riskFactors.Add("Monthly spending is highly volatile, which makes budgeting difficult.");
        }
        else if (coefficientOfVariation > 0.3f)
        {
            score += 1f;
            riskFactors.Add("Monthly spending varies noticeably across months.");
        }
        else
        {
            positiveSignals.Add("Monthly spending is relatively stable.");
        }
    }

    private static void AddCategoryConcentrationRisk(
        UserRiskFeatures features,
        ref float score,
        List<string> riskFactors,
        List<string> positiveSignals)
    {
        if (features.TopCategoryPercentage > 70f)
        {
            score += 2f;
            riskFactors.Add($"{features.TopCategory} dominates spending at {features.TopCategoryPercentage:F1}%.");
        }
        else if (features.TopCategoryPercentage > 50f)
        {
            score += 1f;
            riskFactors.Add($"{features.TopCategory} is concentrated at {features.TopCategoryPercentage:F1}% of spending.");
        }
        else if (features.CategoryCount >= 3)
        {
            positiveSignals.Add("Spending is reasonably distributed across categories.");
        }

        if (features.CategoryCount <= 2 && features.TotalTransactions > 5)
        {
            score += 1f;
            riskFactors.Add("Spending is spread across too few categories, reducing explainability.");
        }
    }

    private static void AddDiscretionarySpendRisk(
        UserRiskFeatures features,
        ref float score,
        List<string> riskFactors,
        List<string> positiveSignals)
    {
        if (features.FoodSpendPercentage > 40f)
        {
            score += 2f;
            riskFactors.Add($"Food and dining spend is high at {features.FoodSpendPercentage:F1}%.");
        }
        else if (features.FoodSpendPercentage > 25f)
        {
            score += 1f;
            riskFactors.Add($"Food and dining spend is above the suggested range at {features.FoodSpendPercentage:F1}%.");
        }

        if (features.EntertainmentSpendPercentage > 30f)
        {
            score += 2f;
            riskFactors.Add($"Entertainment/leisure spend is high at {features.EntertainmentSpendPercentage:F1}%.");
        }
        else if (features.EntertainmentSpendPercentage > 15f)
        {
            score += 1f;
            riskFactors.Add($"Entertainment/leisure spend is slightly elevated at {features.EntertainmentSpendPercentage:F1}%.");
        }

        if (features.FoodSpendPercentage <= 25f && features.EntertainmentSpendPercentage <= 15f)
        {
            positiveSignals.Add("Discretionary spending appears controlled.");
        }
    }

    private static void AddLargeTransactionRisk(
        UserRiskFeatures features,
        ref float score,
        List<string> riskFactors,
        List<string> positiveSignals)
    {
        if (features.LargeTransactionFrequency > 5f)
        {
            score += 2f;
            riskFactors.Add($"Large transactions are frequent at {features.LargeTransactionFrequency:F1} per month.");
        }
        else if (features.LargeTransactionFrequency > 2f)
        {
            score += 1f;
            riskFactors.Add($"Large transactions occur {features.LargeTransactionFrequency:F1} times per month on average.");
        }
        else
        {
            positiveSignals.Add("Large transactions are not frequent.");
        }
    }

    private static void AddTrendRisk(
        UserRiskFeatures features,
        ref float score,
        List<string> riskFactors,
        List<string> positiveSignals)
    {
        if (features.MonthCount >= 2)
        {
            if (features.MoMSpendChangePercentage > 40f)
            {
                score += 2f;
                riskFactors.Add($"Spending increased sharply by {features.MoMSpendChangePercentage:F1}% compared with the previous month.");
            }
            else if (features.MoMSpendChangePercentage > 20f)
            {
                score += 1f;
                riskFactors.Add($"Spending increased by {features.MoMSpendChangePercentage:F1}% compared with the previous month.");
            }
            else if (features.MoMSpendChangePercentage < -20f)
            {
                score -= 1f;
                positiveSignals.Add($"Spending reduced by {Math.Abs(features.MoMSpendChangePercentage):F1}% compared with the previous month.");
            }
        }

        if (features.MonthCount >= 3)
        {
            if (features.SpendingTrend > 0.3f)
            {
                score += 1f;
                riskFactors.Add("Three-month spending trend is increasing.");
            }
            else if (features.SpendingTrend < -0.2f)
            {
                score -= 1f;
                positiveSignals.Add("Three-month spending trend is improving.");
            }
        }
    }

    private static void AddDataQualitySignals(
        UserRiskFeatures features,
        List<string> riskFactors,
        List<string> positiveSignals)
    {
        if (features.MonthCount == 1)
        {
            riskFactors.Add("Only one month of data is available, so trend-based risk is limited.");
        }

        if (features.EssentialSpendPercentage < 10f && features.TotalTransactions > 10)
        {
            riskFactors.Add("Essential spending appears unusually low; categories may need review.");
        }

        if (positiveSignals.Count == 0 && riskFactors.Count == 0)
        {
            positiveSignals.Add("No major financial risk pattern was detected.");
        }
    }
}

public class RiskAssessmentResult
{
    public string RiskLevel { get; set; } = "Unknown";
    public float RiskScorePercent { get; set; }
    public float RawRiskPoints { get; set; }
    public float MaxRiskPoints { get; set; }
    public string Summary { get; set; } = string.Empty;
    public List<string> RiskFactors { get; set; } = new();
    public List<string> PositiveSignals { get; set; } = new();
}
