using Microsoft.ML.Data;

// These classes define the schema ML.NET uses for training and prediction
// ML.NET requires explicit column mappings via attributes

namespace Backend.Models.ML
{
    // Input features fed into the model
    public class RiskInput
    {
        [LoadColumn(0)]
        public float MonthlyAvgSpend { get; set; }

        [LoadColumn(1)]
        public float TransactionFrequency { get; set; }   // transactions per month

        [LoadColumn(2)]
        public float TopCategoryPercentage { get; set; }  // % of spend in top category

        [LoadColumn(3)]
        public float CategoryCount { get; set; }          // how spread spending is

        [LoadColumn(4)]
        public float Label { get; set; }                  // 0=Low, 1=Medium, 2=High
    }

    // What ML.NET outputs after prediction
    public class RiskOutput
    {
        [ColumnName("PredictedLabel")]
        public float PredictedLabel { get; set; }

        public float[] Score { get; set; } = Array.Empty<float>();
    }
}