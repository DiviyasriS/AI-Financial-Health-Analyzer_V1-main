using Backend.Models.ML;

/// <summary>
/// Converts a user's raw transaction history into a structured feature vector
/// suitable for ML.NET risk classification.
/// 
/// Design decisions:
/// - All computation is pure (no I/O, no DI), so it is easily unit-testable.
/// - Categories are matched case-insensitively via keyword lists.
/// - When fewer than 2 months of data exist, trend/MoM features default to 0.
/// - Income is not assumed; the model is trained and predicts without it.
/// </summary>
public static class FinancialFeatureExtractor
{
    // ── Category keyword maps ────────────────────────────────────────────────
    private static readonly HashSet<string> EssentialKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "groceries", "grocery", "supermarket", "utilities", "electricity", "water",
        "gas", "rent", "mortgage", "insurance", "medical", "pharmacy", "transport",
        "bus", "train", "metro", "fuel", "petrol", "internet", "phone", "mobile"
    };

    private static readonly HashSet<string> FoodKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "restaurant", "food", "dining", "cafe", "coffee", "swiggy", "zomato",
        "delivery", "pizza", "burger", "lunch", "dinner", "breakfast", "snack",
        "hotel", "eatery", "bistro", "bakery", "takeaway"
    };

    private static readonly HashSet<string> EntertainmentKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "entertainment", "netflix", "spotify", "amazon prime", "hotstar",
        "movie", "cinema", "theatre", "concert", "gaming", "game", "subscription",
        "streaming", "gym", "fitness", "leisure", "bar", "pub", "club", "party"
    };

    /// <summary>
    /// Derives all ML features from a user's complete transaction list.
    /// </summary>
    /// <param name="transactions">All transactions for the user, ordered arbitrarily.</param>
    /// <returns>A fully populated <see cref="UserRiskFeatures"/> instance.</returns>
    public static UserRiskFeatures Extract(List<Transaction> transactions)
    {
        if (transactions == null || transactions.Count == 0)
        {
            return new UserRiskFeatures();
        }

        // ── Monthly aggregation ──────────────────────────────────────────────
        Dictionary<(int Year, int Month), List<Transaction>> byMonth =
            transactions
                .GroupBy(t => (t.Date.Year, t.Date.Month))
                .ToDictionary(g => g.Key, g => g.ToList());

        List<decimal> monthlyTotals = byMonth
            .OrderBy(kv => kv.Key.Year).ThenBy(kv => kv.Key.Month)
            .Select(kv => kv.Value.Sum(t => Math.Abs(t.Amount)))
            .ToList();

        int monthCount = monthlyTotals.Count;
        double monthlyAvg = monthCount > 0
            ? (double)monthlyTotals.Average()
            : 0.0;

        double monthlyStdDev = ComputeStdDev(monthlyTotals.Select(m => (double)m).ToList(), monthlyAvg);

        // ── Transaction frequency ────────────────────────────────────────────
        float avgTxnsPerMonth = monthCount > 0
            ? (float)transactions.Count / monthCount
            : transactions.Count;

        double avgTxnAmount = transactions.Count > 0
            ? (double)transactions.Average(t => Math.Abs(t.Amount))
            : 0.0;

        float largeThreshold = (float)(avgTxnAmount * 2.0);
        int largeTransactionCount = transactions.Count(t => (float)Math.Abs(t.Amount) > largeThreshold);
        float largeTransactionFrequency = monthCount > 0
            ? (float)largeTransactionCount / monthCount
            : largeTransactionCount;

        // ── Category breakdown ───────────────────────────────────────────────
        decimal totalSpend = transactions.Sum(t => Math.Abs(t.Amount));

        Dictionary<string, decimal> byCategory = transactions
            .GroupBy(t => NormaliseCategory(t.Category, t.Description))
            .ToDictionary(g => g.Key, g => g.Sum(t => Math.Abs(t.Amount)));

        KeyValuePair<string, decimal> topCategory = byCategory.Count > 0
            ? byCategory.OrderByDescending(kv => kv.Value).First()
            : new KeyValuePair<string, decimal>("Uncategorized", 0m);

        float topCategoryPct = totalSpend > 0
            ? (float)((topCategory.Value / totalSpend) * 100m)
            : 0f;

        // ── Composition percentages ──────────────────────────────────────────
        float essentialPct  = ComputeCategoryGroupPct(transactions, totalSpend, EssentialKeywords);
        float foodPct       = ComputeCategoryGroupPct(transactions, totalSpend, FoodKeywords);
        float entertainPct  = ComputeCategoryGroupPct(transactions, totalSpend, EntertainmentKeywords);

        // ── Month-over-month change ──────────────────────────────────────────
        float momChange = 0f;
        if (monthlyTotals.Count >= 2)
        {
            decimal latest   = monthlyTotals[^1];
            decimal previous = monthlyTotals[^2];
            momChange = previous > 0
                ? (float)(((latest - previous) / previous) * 100m)
                : 0f;
        }

        // ── 3-month spending trend (linear slope, normalised) ────────────────
        float spendingTrend = ComputeNormalisedTrend(monthlyTotals);

        return new UserRiskFeatures
        {
            MonthlyAvgSpend              = (float)monthlyAvg,
            MonthlySpendStdDev           = (float)monthlyStdDev,
            TransactionFrequency         = avgTxnsPerMonth,
            LargeTransactionFrequency    = largeTransactionFrequency,
            TopCategoryPercentage        = topCategoryPct,
            CategoryCount                = byCategory.Count,
            EssentialSpendPercentage     = essentialPct,
            FoodSpendPercentage          = foodPct,
            EntertainmentSpendPercentage = entertainPct,
            MoMSpendChangePercentage     = momChange,
            SpendingTrend                = spendingTrend,
            TopCategory                  = topCategory.Key,
            TotalSpend                   = totalSpend,
            TotalTransactions            = transactions.Count,
            MonthCount                   = monthCount,
            MonthlyTotals                = monthlyTotals
        };
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Maps a raw category string and description to a normalised group name.
    /// Falls back to the trimmed category value if no keyword matches.
    /// </summary>
    private static string NormaliseCategory(string category, string description)
    {
        string combined = $"{category} {description}".ToLowerInvariant();

        if (ContainsAny(combined, EssentialKeywords))  return "Essential";
        if (ContainsAny(combined, FoodKeywords))        return "Food";
        if (ContainsAny(combined, EntertainmentKeywords)) return "Entertainment";

        return string.IsNullOrWhiteSpace(category) ? "Other" : category.Trim();
    }

    private static bool ContainsAny(string text, HashSet<string> keywords)
    {
        foreach (string keyword in keywords)
        {
            if (text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static float ComputeCategoryGroupPct(
        List<Transaction> transactions,
        decimal totalSpend,
        HashSet<string> keywords)
    {
        if (totalSpend <= 0) return 0f;

        decimal groupTotal = transactions
            .Where(t => ContainsAny($"{t.Category} {t.Description}", keywords))
            .Sum(t => Math.Abs(t.Amount));

        return (float)((groupTotal / totalSpend) * 100m);
    }

    private static double ComputeStdDev(List<double> values, double mean)
    {
        if (values.Count < 2) return 0.0;
        double sumSq = values.Sum(v => (v - mean) * (v - mean));
        return Math.Sqrt(sumSq / values.Count);
    }

    /// <summary>
    /// Computes a normalised linear trend from monthly totals.
    /// Returns a value in roughly [-1, 1]: positive = increasing spend, negative = decreasing.
    /// Uses the last 3 months only; falls back to 0 when fewer than 2 months available.
    /// </summary>
    private static float ComputeNormalisedTrend(List<decimal> monthlyTotals)
    {
        List<decimal> window = monthlyTotals.Count >= 3
            ? monthlyTotals.TakeLast(3).ToList()
            : monthlyTotals;

        if (window.Count < 2) return 0f;

        // Simple least-squares slope
        int n = window.Count;
        double sumX  = 0, sumY = 0, sumXY = 0, sumX2 = 0;
        for (int i = 0; i < n; i++)
        {
            sumX  += i;
            sumY  += (double)window[i];
            sumXY += i * (double)window[i];
            sumX2 += i * i;
        }

        double denom = n * sumX2 - sumX * sumX;
        if (Math.Abs(denom) < 1e-9) return 0f;

        double slope = (n * sumXY - sumX * sumY) / denom;
        double avgY  = sumY / n;

        // Normalise by the average so it is scale-independent
        return avgY > 1.0
            ? (float)(slope / avgY)
            : 0f;
    }
}
