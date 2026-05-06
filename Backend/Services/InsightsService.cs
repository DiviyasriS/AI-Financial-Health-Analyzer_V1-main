using Microsoft.Extensions.Logging;

// InsightsService generates human-readable insights from spending data + risk level
// Logic is rule-based — simple, readable, and easy to extend
// Each rule checks a specific condition and produces a prioritized insight

public class InsightsService
{
    private readonly IInsightRepository _insightRepository;
    private readonly ILogger<InsightsService> _logger;

    public InsightsService(
        IInsightRepository insightRepository,
        ILogger<InsightsService> logger)
    {
        _insightRepository = insightRepository;
        _logger = logger;
    }

    public async Task<List<Insight>> GenerateAndSaveAsync(
        int userId,
        SpendingSummaryDto summary,
        string riskLevel)
    {
        _logger.LogInformation(
            "Generating insights for UserId={UserId}, RiskLevel={Risk}",
            userId, riskLevel);

        var insights = new List<Insight>();
        int priority = 1;

        // ── Rule 1: High risk warning ─────────────────────────────────────
        if (riskLevel == "High")
        {
            insights.Add(new Insight
            {
                UserId   = userId,
                Title    = "High Financial Risk Detected",
                Message  = $"Your spending patterns indicate high financial risk. " +
                           $"Your average monthly spend is ₹{summary.AverageMonthlySpend:F0}. " +
                           $"Consider reducing discretionary expenses immediately.",
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
                           "Review your largest expense categories and consider setting a monthly budget.",
                Priority = priority++,
                Type     = "warning"
            });
        }

        // ── Rule 2: Overspending in top category ──────────────────────────
        if (summary.CategoryBreakdown.Count > 0)
        {
            var topCat = summary.CategoryBreakdown[0];
            if (topCat.PercentageOfTotal > 50)
            {
                insights.Add(new Insight
                {
                    UserId   = userId,
                    Title    = $"Overspending on {topCat.Category}",
                    Message  = $"{topCat.Category} accounts for {topCat.PercentageOfTotal}% " +
                               $"of your total spend (₹{topCat.Total:F0}). " +
                               $"A healthy budget keeps any single category below 50%.",
                    Priority = priority++,
                    Type     = "warning"
                });
            }
        }

        // ── Rule 3: Month-over-month spike ────────────────────────────────
        if (summary.MonthlyBreakdown.Count >= 2)
        {
            var latest   = summary.MonthlyBreakdown[0];
            var previous = summary.MonthlyBreakdown[1];

            if (previous.Total > 0)
            {
                var changePercent = ((latest.Total - previous.Total) / previous.Total) * 100;
                if (changePercent > 30)
                {
                    insights.Add(new Insight
                    {
                        UserId   = userId,
                        Title    = "Unusual Spending Spike",
                        Message  = $"Your spending in {latest.MonthName} increased by " +
                                   $"{changePercent:F1}% compared to {previous.MonthName}. " +
                                   $"Review your recent transactions for unexpected charges.",
                        Priority = priority++,
                        Type     = "warning"
                    });
                }
                else if (changePercent < -20)
                {
                    insights.Add(new Insight
                    {
                        UserId   = userId,
                        Title    = "Spending Decreased This Month",
                        Message  = $"Great work! Your spending dropped by {Math.Abs(changePercent):F1}% " +
                                   $"in {latest.MonthName}. Keep maintaining this discipline.",
                        Priority = priority++,
                        Type     = "info"
                    });
                }
            }
        }

        // ── Rule 4: Low category diversity ───────────────────────────────
        if (summary.CategoryBreakdown.Count <= 2 && summary.TotalTransactions > 5)
        {
            insights.Add(new Insight
            {
                UserId   = userId,
                Title    = "Low Spending Diversity",
                Message  = $"Your spending is concentrated in only {summary.CategoryBreakdown.Count} " +
                           $"category/categories. Diversifying expenses often indicates healthier finances.",
                Priority = priority++,
                Type     = "info"
            });
        }

        // ── Rule 5: Positive — low risk acknowledgement ───────────────────
        if (riskLevel == "Low" && summary.CategoryBreakdown.Count >= 3)
        {
            insights.Add(new Insight
            {
                UserId   = userId,
                Title    = "Healthy Spending Pattern",
                Message  = $"Your spending is well distributed across {summary.CategoryBreakdown.Count} " +
                           $"categories with a manageable monthly average of ₹{summary.AverageMonthlySpend:F0}. " +
                           $"Keep it up!",
                Priority = priority++,
                Type     = "info"
            });
        }

        // Delete old insights and save fresh ones
        await _insightRepository.DeleteByUserIdAsync(userId);
        await _insightRepository.SaveRangeAsync(insights);

        return insights;
    }
}