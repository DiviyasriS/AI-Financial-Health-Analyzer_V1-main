using Backend.Models.ML;

/// <summary>
/// Generates human-readable, explainable financial insights driven by the same
/// features used in the ML risk model.
///
/// Design:
/// - Each rule checks a specific feature threshold and maps it to a named insight.
/// - Insights reference the same metric the ML model used ("food spending", "MoM change")
///   so the user understands *why* their risk is high.
/// - Insights are sorted by priority (1 = most urgent).
/// - Old insights are deleted before new ones are saved (per-user replacement).
/// </summary>
public class InsightsService
{
    private readonly IInsightRepository _insightRepository;
    private readonly ILogger<InsightsService> _logger;

    public InsightsService(
        IInsightRepository insightRepository,
        ILogger<InsightsService> logger)
    {
        _insightRepository = insightRepository;
        _logger            = logger;
    }

    /// <summary>
    /// Generates, persists, and returns insights for a user based on their
    /// feature vector and ML-derived risk level.
    /// </summary>
    public async Task<List<Insight>> GenerateAndSaveAsync(
        int userId,
        UserRiskFeatures features,
        SpendingSummaryDto summary,
        string riskLevel)
    {
        _logger.LogInformation(
            "Generating insights for UserId={UserId}, RiskLevel={Risk}",
            userId, riskLevel);

        List<Insight> insights = BuildInsights(userId, features, summary, riskLevel);

        await _insightRepository.DeleteByUserIdAsync(userId);
        await _insightRepository.SaveRangeAsync(insights);

        return insights;
    }

    // ── Private ──────────────────────────────────────────────────────────────

    private static List<Insight> BuildInsights(
        int userId,
        UserRiskFeatures features,
        SpendingSummaryDto summary,
        string riskLevel)
    {
        List<Insight> insights = new();
        int priority = 1;

        // ── Overall risk level ───────────────────────────────────────────────
        if (riskLevel == "High")
        {
            insights.Add(new Insight
            {
                UserId   = userId,
                Title    = "High Financial Risk Detected",
                Message  = $"Your spending patterns indicate high financial risk. " +
                           $"Your monthly average is ₹{features.MonthlyAvgSpend:F0}. " +
                           $"Review the insights below to understand which areas need attention.",
                Priority = priority++,
                Type     = "danger"
            });
        }
        else if (riskLevel == "Medium")
        {
            insights.Add(new Insight
            {
                UserId   = userId,
                Title    = "Moderate Financial Risk",
                Message  = "Your spending shows moderate risk. " +
                           "A few spending habits are driving this — see below for specifics.",
                Priority = priority++,
                Type     = "warning"
            });
        }

        // ── Food spending ────────────────────────────────────────────────────
        if (features.FoodSpendPercentage > 40f)
        {
            insights.Add(new Insight
            {
                UserId   = userId,
                Title    = "High Food & Dining Spending",
                Message  = $"Food and dining accounts for {features.FoodSpendPercentage:F1}% " +
                           $"of your total spend. A healthy budget keeps dining below 25-30%. " +
                           $"Consider meal planning or reducing takeaway orders.",
                Priority = priority++,
                Type     = "danger"
            });
        }
        else if (features.FoodSpendPercentage > 25f)
        {
            insights.Add(new Insight
            {
                UserId   = userId,
                Title    = "Food Spending Above Average",
                Message  = $"Food and dining is {features.FoodSpendPercentage:F1}% of your spend. " +
                           $"Consider reviewing your dining frequency.",
                Priority = priority++,
                Type     = "warning"
            });
        }

        // ── Entertainment spending ───────────────────────────────────────────
        if (features.EntertainmentSpendPercentage > 30f)
        {
            insights.Add(new Insight
            {
                UserId   = userId,
                Title    = "Entertainment Spending is Concentrated",
                Message  = $"Entertainment (subscriptions, leisure, etc.) is " +
                           $"{features.EntertainmentSpendPercentage:F1}% of your spend. " +
                           $"This is significantly above the recommended 10-15%. " +
                           $"Review active subscriptions and discretionary purchases.",
                Priority = priority++,
                Type     = "danger"
            });
        }
        else if (features.EntertainmentSpendPercentage > 15f)
        {
            insights.Add(new Insight
            {
                UserId   = userId,
                Title    = "Entertainment Spending Slightly Elevated",
                Message  = $"Entertainment is {features.EntertainmentSpendPercentage:F1}% of your spend. " +
                           $"Watch for subscription creep.",
                Priority = priority++,
                Type     = "warning"
            });
        }

        // ── Category concentration ───────────────────────────────────────────
        if (features.TopCategoryPercentage > 70f)
        {
            insights.Add(new Insight
            {
                UserId   = userId,
                Title    = $"Spending Heavily Concentrated in {features.TopCategory}",
                Message  = $"{features.TopCategory} accounts for {features.TopCategoryPercentage:F1}% " +
                           $"of all your spending. High concentration in one category reduces " +
                           $"financial flexibility. Try diversifying your budget.",
                Priority = priority++,
                Type     = "danger"
            });
        }
        else if (features.TopCategoryPercentage > 50f)
        {
            insights.Add(new Insight
            {
                UserId   = userId,
                Title    = $"Top Category Dominance: {features.TopCategory}",
                Message  = $"{features.TopCategory} is {features.TopCategoryPercentage:F1}% of spend. " +
                           $"A healthy budget keeps any category below 50%.",
                Priority = priority++,
                Type     = "warning"
            });
        }

        // ── Month-over-month spike ───────────────────────────────────────────
        if (features.MoMSpendChangePercentage > 40f)
        {
            insights.Add(new Insight
            {
                UserId   = userId,
                Title    = "Sharp Spending Increase This Month",
                Message  = $"Your spending increased by {features.MoMSpendChangePercentage:F1}% " +
                           $"compared to last month. This sharp increase significantly raised " +
                           $"your risk score. Review recent large transactions for unexpected charges.",
                Priority = priority++,
                Type     = "danger"
            });
        }
        else if (features.MoMSpendChangePercentage > 20f)
        {
            insights.Add(new Insight
            {
                UserId   = userId,
                Title    = "Spending Increased vs Last Month",
                Message  = $"Your spending rose by {features.MoMSpendChangePercentage:F1}% " +
                           $"compared to the previous month.",
                Priority = priority++,
                Type     = "warning"
            });
        }
        else if (features.MoMSpendChangePercentage < -20f)
        {
            insights.Add(new Insight
            {
                UserId   = userId,
                Title    = "Great Progress — Spending Decreased",
                Message  = $"Your spending dropped by {Math.Abs(features.MoMSpendChangePercentage):F1}% " +
                           $"compared to last month. Keep it up!",
                Priority = priority++,
                Type     = "info"
            });
        }

        // ── Spending trend (3-month) ─────────────────────────────────────────
        if (features.SpendingTrend > 0.3f && features.MonthCount >= 3)
        {
            insights.Add(new Insight
            {
                UserId   = userId,
                Title    = "Upward Spending Trend Over 3 Months",
                Message  = "Your spending has been consistently increasing over the past 3 months. " +
                           "This sustained trend increases your long-term financial risk. " +
                           "Set a monthly budget cap to reverse this trend.",
                Priority = priority++,
                Type     = "warning"
            });
        }

        // ── High spending volatility ─────────────────────────────────────────
        float cvSpend = features.MonthlyAvgSpend > 0
            ? features.MonthlySpendStdDev / features.MonthlyAvgSpend
            : 0f;

        if (cvSpend > 0.6f)
        {
            insights.Add(new Insight
            {
                UserId   = userId,
                Title    = "Highly Volatile Monthly Spending",
                Message  = $"Your monthly spending varies significantly (std dev: " +
                           $"₹{features.MonthlySpendStdDev:F0} vs avg ₹{features.MonthlyAvgSpend:F0}). " +
                           $"Unpredictable spending makes budgeting difficult. " +
                           $"Try to smooth expenses across months.",
                Priority = priority++,
                Type     = "warning"
            });
        }

        // ── Large transactions ───────────────────────────────────────────────
        if (features.LargeTransactionFrequency > 5f)
        {
            insights.Add(new Insight
            {
                UserId   = userId,
                Title    = "Frequent Large Transactions",
                Message  = $"You average {features.LargeTransactionFrequency:F1} large transactions " +
                           $"(>2× your avg transaction size) per month. " +
                           $"Frequent large purchases strain cash flow. " +
                           $"Consider spreading major purchases over time.",
                Priority = priority++,
                Type     = "warning"
            });
        }

        // ── Low category diversity ───────────────────────────────────────────
        if (features.CategoryCount <= 2 && features.TotalTransactions > 5)
        {
            insights.Add(new Insight
            {
                UserId   = userId,
                Title    = "Low Spending Diversity",
                Message  = $"Your spending is concentrated in only {features.CategoryCount} " +
                           $"category/categories. Diverse spending patterns generally indicate " +
                           $"healthier financial behaviour.",
                Priority = priority++,
                Type     = "info"
            });
        }

        // ── Healthy pattern acknowledgement ─────────────────────────────────
        if (riskLevel == "Low" && features.CategoryCount >= 3)
        {
            insights.Add(new Insight
            {
                UserId   = userId,
                Title    = "Healthy Spending Pattern",
                Message  = $"Your spending is well distributed across {features.CategoryCount} categories " +
                           $"with a manageable monthly average of ₹{features.MonthlyAvgSpend:F0}. " +
                           $"Your food and entertainment spending are within healthy limits. Keep it up!",
                Priority = priority++,
                Type     = "info"
            });
        }

        return insights
            .OrderBy(i => i.Priority)
            .ToList();
    }
}