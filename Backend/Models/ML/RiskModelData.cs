using Microsoft.ML.Data;

namespace Backend.Models.ML
{
    /// <summary>
    /// Input features fed into the ML.NET risk classification model.
    /// All fields are float because ML.NET trainers require numeric float features.
    /// Features are engineered from real user transaction history.
    /// </summary>
    public class RiskInput
    {
        // ── Spending magnitude ──────────────────────────────────────────────
        [LoadColumn(0)]
        public float MonthlyAvgSpend { get; set; }

        [LoadColumn(1)]
        public float MonthlySpendStdDev { get; set; }          // Variance in monthly spend

        // ── Frequency ───────────────────────────────────────────────────────
        [LoadColumn(2)]
        public float TransactionFrequency { get; set; }         // Avg transactions per month

        [LoadColumn(3)]
        public float LargeTransactionFrequency { get; set; }    // Txns > 2× avg per month

        // ── Category concentration ──────────────────────────────────────────
        [LoadColumn(4)]
        public float TopCategoryPercentage { get; set; }        // % of spend in top category

        [LoadColumn(5)]
        public float CategoryCount { get; set; }                // Number of distinct categories

        // ── Spending composition ────────────────────────────────────────────
        [LoadColumn(6)]
        public float EssentialSpendPercentage { get; set; }     // Groceries, utilities, rent, transport

        [LoadColumn(7)]
        public float FoodSpendPercentage { get; set; }          // Restaurants, food delivery, cafes

        [LoadColumn(8)]
        public float EntertainmentSpendPercentage { get; set; } // Entertainment, subscriptions, leisure

        // ── Trend ───────────────────────────────────────────────────────────
        [LoadColumn(9)]
        public float MoMSpendChangePercentage { get; set; }     // Month-over-month % change (latest vs prev)

        [LoadColumn(10)]
        public float SpendingTrend { get; set; }                // 3-month linear trend slope (normalised)

        // ── Label (used during training only) ───────────────────────────────
        [LoadColumn(11)]
        public float Label { get; set; }                        // 0=Low, 1=Medium, 2=High
    }

    /// <summary>
    /// Output produced by the trained ML.NET model during prediction.
    /// SdcaMaximumEntropy outputs calibrated per-class probabilities via Softmax.
    /// Index 0 = Low, 1 = Medium, 2 = High (when KeyOrdinality.ByValue is used with labels 0,1,2).
    /// </summary>
    public class RiskOutput
    {
        [ColumnName("PredictedLabel")]
        public float PredictedLabel { get; set; }

        [ColumnName("Score")]
        public float[] Score { get; set; } = Array.Empty<float>();
    }

    /// <summary>
    /// Rich feature vector computed from a user's real transaction history.
    /// Passed from FinancialFeatureExtractor → RiskPredictionService.
    /// </summary>
    public class UserRiskFeatures
    {
        public float MonthlyAvgSpend { get; set; }
        public float MonthlySpendStdDev { get; set; }
        public float TransactionFrequency { get; set; }
        public float LargeTransactionFrequency { get; set; }
        public float TopCategoryPercentage { get; set; }
        public float CategoryCount { get; set; }
        public float EssentialSpendPercentage { get; set; }
        public float FoodSpendPercentage { get; set; }
        public float EntertainmentSpendPercentage { get; set; }
        public float MoMSpendChangePercentage { get; set; }
        public float SpendingTrend { get; set; }

        // Contextual data for insight generation (not fed into ML model directly)
        public string TopCategory { get; set; } = string.Empty;
        public decimal TotalSpend { get; set; }
        public int TotalTransactions { get; set; }
        public int MonthCount { get; set; }
        public List<decimal> MonthlyTotals { get; set; } = new();
    }
}