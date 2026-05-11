using Backend.Models.ML;

/// <summary>
/// Generates risk classification labels (Low / Medium / High) from a feature vector.
/// 
/// Labels are computed via a transparent rule scorecard so that:
/// 1. Domain knowledge is explicit and auditable.
/// 2. The ML model learns patterns that generalise beyond the rules.
/// 3. Labels improve automatically as transaction history grows.
///
/// Scoring: accumulate risk points, then threshold into 3 classes.
/// </summary>
public static class RiskLabelGenerator
{
    // ── Label constants (must match ML training) ─────────────────────────────
    public const float LabelLow    = 0f;
    public const float LabelMedium = 1f;
    public const float LabelHigh   = 2f;

    /// <summary>
    /// Derives a risk label from a computed feature vector.
    /// Returns 0 (Low), 1 (Medium), or 2 (High).
    /// </summary>
    public static float GenerateLabel(UserRiskFeatures features)
    {
        float score = 0f;

        // ── Spending magnitude ───────────────────────────────────────────────
        if      (features.MonthlyAvgSpend > 60_000f) score += 3f;
        else if (features.MonthlyAvgSpend > 30_000f) score += 2f;
        else if (features.MonthlyAvgSpend > 15_000f) score += 1f;

        // ── Spending volatility ──────────────────────────────────────────────
        // High variance relative to mean → unpredictable cash flow
        float cvSpend = features.MonthlyAvgSpend > 0
            ? features.MonthlySpendStdDev / features.MonthlyAvgSpend
            : 0f;
        if      (cvSpend > 0.6f) score += 2f;
        else if (cvSpend > 0.3f) score += 1f;

        // ── Category concentration ───────────────────────────────────────────
        if      (features.TopCategoryPercentage > 70f) score += 2f;
        else if (features.TopCategoryPercentage > 50f) score += 1f;

        if (features.CategoryCount <= 2 && features.TotalTransactions > 5) score += 1f;

        // ── Non-essential spending ───────────────────────────────────────────
        if      (features.FoodSpendPercentage > 40f)        score += 2f;
        else if (features.FoodSpendPercentage > 25f)        score += 1f;

        if      (features.EntertainmentSpendPercentage > 30f) score += 2f;
        else if (features.EntertainmentSpendPercentage > 15f) score += 1f;

        // ── Large transactions ───────────────────────────────────────────────
        if      (features.LargeTransactionFrequency > 5f) score += 2f;
        else if (features.LargeTransactionFrequency > 2f) score += 1f;

        // ── Month-over-month trend ───────────────────────────────────────────
        if      (features.MoMSpendChangePercentage > 40f) score += 2f;
        else if (features.MoMSpendChangePercentage > 20f) score += 1f;
        else if (features.MoMSpendChangePercentage < -20f) score -= 1f; // improvement

        // ── 3-month trend slope ──────────────────────────────────────────────
        if      (features.SpendingTrend > 0.3f)  score += 1f;
        else if (features.SpendingTrend < -0.2f) score -= 1f; // improving

        // ── Low essential spend (possible under-reporting of income needs) ───
        if (features.EssentialSpendPercentage < 10f && features.TotalTransactions > 10)
            score += 1f;

        // ── Classify ─────────────────────────────────────────────────────────
        float safeScore = Math.Max(0f, score); // negative scores become 0

        if      (safeScore >= 7f) return LabelHigh;
        else if (safeScore >= 3f) return LabelMedium;
        else                      return LabelLow;
    }
}
