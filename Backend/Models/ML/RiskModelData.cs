using Microsoft.ML.Data;

namespace Backend.Models.ML
{
    public class RiskInput
    {
        [LoadColumn(0)] public float MonthlyAvgSpend { get; set; }
        [LoadColumn(1)] public float TransactionFrequency { get; set; }
        [LoadColumn(2)] public float TopCategoryPercentage { get; set; }
        [LoadColumn(3)] public float CategoryCount { get; set; }
        [LoadColumn(4)] public float Label { get; set; }
    }

    public class RiskOutput
    {
        [ColumnName("PredictedLabel")]
        public float PredictedLabel { get; set; }

        // SdcaMaximumEntropy outputs softmax probabilities — one per class
        // Index 0 = Low, Index 1 = Medium, Index 2 = High
        // (when KeyOrdinality.ByValue is used with labels 0, 1, 2)
        public float[] Score { get; set; } = Array.Empty<float>();
    }
}