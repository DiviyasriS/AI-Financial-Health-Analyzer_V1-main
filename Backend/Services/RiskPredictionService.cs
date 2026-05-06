using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.Extensions.Logging;
using Backend.Models.ML;

// RiskPredictionService encapsulates all ML.NET logic
// It trains a simple multi-class classification model on synthetic data
// then predicts risk for a real user based on their spending summary
//
// Why synthetic training data?
// In a PoC, we don't have labelled historical data from real users.
// We generate representative samples that reflect known financial risk patterns.
// This is standard practice for early-stage ML systems.

public class RiskPredictionService
{
    private readonly MLContext _mlContext;
    private readonly ILogger<RiskPredictionService> _logger;
    private ITransformer? _trainedModel;
    private DataViewSchema? _modelSchema;

    public RiskPredictionService(ILogger<RiskPredictionService> logger)
    {
        _mlContext = new MLContext(seed: 42); // seed for reproducibility
        _logger = logger;
        TrainModel();
    }

    // ─── TRAINING ─────────────────────────────────────────────────────────

    private void TrainModel()
    {
        _logger.LogInformation("Training risk prediction model...");

        // Generate synthetic training data representing spending risk patterns
        var trainingData = GenerateSyntheticTrainingData();

        var dataView = _mlContext.Data.LoadFromEnumerable(trainingData);

        // Build the ML pipeline:
        // 1. Combine input features into a single "Features" vector
        // 2. Convert the float Label to a key type for multi-class classification
        // 3. Train using SDCA (fast, works well for tabular data)
        var pipeline = _mlContext.Transforms
            .Concatenate("Features",
                nameof(RiskInput.MonthlyAvgSpend),
                nameof(RiskInput.TransactionFrequency),
                nameof(RiskInput.TopCategoryPercentage),
                nameof(RiskInput.CategoryCount))
            .Append(_mlContext.Transforms.Conversion.MapValueToKey("Label"))
            .Append(_mlContext.MulticlassClassification.Trainers.SdcaMaximumEntropy(
                labelColumnName: "Label",
                featureColumnName: "Features"))
            .Append(_mlContext.Transforms.Conversion.MapKeyToValue("PredictedLabel"));

        _trainedModel = pipeline.Fit(dataView);
        _modelSchema = dataView.Schema;

        _logger.LogInformation("Risk prediction model training complete.");
    }

    // ─── PREDICTION ───────────────────────────────────────────────────────

    public (string riskLevel, float riskScore) Predict(
        decimal monthlyAvgSpend,
        int totalTransactions,
        int monthCount,
        float topCategoryPercentage,
        int categoryCount)
    {
        if (_trainedModel == null)
        {
            _logger.LogWarning("Model not trained. Returning default Low risk.");
            return ("Low", 0.1f);
        }

        var transactionFrequency = monthCount > 0
            ? (float)totalTransactions / monthCount
            : (float)totalTransactions;

        var input = new RiskInput
        {
            MonthlyAvgSpend       = (float)monthlyAvgSpend,
            TransactionFrequency  = transactionFrequency,
            TopCategoryPercentage = topCategoryPercentage,
            CategoryCount         = (float)categoryCount
        };

        var predictionEngine = _mlContext.Model
            .CreatePredictionEngine<RiskInput, RiskOutput>(
                _trainedModel, _modelSchema);

        var result = predictionEngine.Predict(input);

        // PredictedLabel: 0 = Low, 1 = Medium, 2 = High
        var riskLevel = result.PredictedLabel switch
        {
            0 => "Low",
            1 => "Medium",
            2 => "High",
            _ => "Low"
        };

        // Use the max score as confidence indicator
        var riskScore = result.Score.Length > 0
            ? result.Score.Max()
            : 0.5f;

        _logger.LogInformation(
            "Risk prediction: Level={Level}, Score={Score}",
            riskLevel, riskScore);

        return (riskLevel, riskScore);
    }

    // ─── SYNTHETIC TRAINING DATA ──────────────────────────────────────────

    private List<RiskInput> GenerateSyntheticTrainingData()
    {
        // Rules used to label the data:
        // LOW    — low monthly spend, spread across categories, moderate frequency
        // MEDIUM — moderate spend or some concentration in one category
        // HIGH   — high spend, or very concentrated in one category, or very high frequency

        return new List<RiskInput>
        {
            // ── LOW risk samples ──────────────────────────────────────────
            new() { MonthlyAvgSpend = 5000,  TransactionFrequency = 8,  TopCategoryPercentage = 30, CategoryCount = 4, Label = 0 },
            new() { MonthlyAvgSpend = 8000,  TransactionFrequency = 10, TopCategoryPercentage = 35, CategoryCount = 5, Label = 0 },
            new() { MonthlyAvgSpend = 3000,  TransactionFrequency = 5,  TopCategoryPercentage = 25, CategoryCount = 4, Label = 0 },
            new() { MonthlyAvgSpend = 6000,  TransactionFrequency = 7,  TopCategoryPercentage = 40, CategoryCount = 4, Label = 0 },
            new() { MonthlyAvgSpend = 4500,  TransactionFrequency = 6,  TopCategoryPercentage = 28, CategoryCount = 5, Label = 0 },
            new() { MonthlyAvgSpend = 7000,  TransactionFrequency = 9,  TopCategoryPercentage = 33, CategoryCount = 5, Label = 0 },
            new() { MonthlyAvgSpend = 2500,  TransactionFrequency = 4,  TopCategoryPercentage = 20, CategoryCount = 3, Label = 0 },
            new() { MonthlyAvgSpend = 9000,  TransactionFrequency = 12, TopCategoryPercentage = 38, CategoryCount = 6, Label = 0 },

            // ── MEDIUM risk samples ───────────────────────────────────────
            new() { MonthlyAvgSpend = 15000, TransactionFrequency = 15, TopCategoryPercentage = 55, CategoryCount = 3, Label = 1 },
            new() { MonthlyAvgSpend = 20000, TransactionFrequency = 20, TopCategoryPercentage = 50, CategoryCount = 4, Label = 1 },
            new() { MonthlyAvgSpend = 12000, TransactionFrequency = 18, TopCategoryPercentage = 60, CategoryCount = 3, Label = 1 },
            new() { MonthlyAvgSpend = 18000, TransactionFrequency = 22, TopCategoryPercentage = 48, CategoryCount = 3, Label = 1 },
            new() { MonthlyAvgSpend = 25000, TransactionFrequency = 14, TopCategoryPercentage = 45, CategoryCount = 4, Label = 1 },
            new() { MonthlyAvgSpend = 10000, TransactionFrequency = 25, TopCategoryPercentage = 65, CategoryCount = 2, Label = 1 },
            new() { MonthlyAvgSpend = 16000, TransactionFrequency = 17, TopCategoryPercentage = 52, CategoryCount = 3, Label = 1 },
            new() { MonthlyAvgSpend = 22000, TransactionFrequency = 19, TopCategoryPercentage = 58, CategoryCount = 3, Label = 1 },

            // ── HIGH risk samples ─────────────────────────────────────────
            new() { MonthlyAvgSpend = 50000, TransactionFrequency = 35, TopCategoryPercentage = 80, CategoryCount = 2, Label = 2 },
            new() { MonthlyAvgSpend = 75000, TransactionFrequency = 40, TopCategoryPercentage = 85, CategoryCount = 1, Label = 2 },
            new() { MonthlyAvgSpend = 40000, TransactionFrequency = 30, TopCategoryPercentage = 75, CategoryCount = 2, Label = 2 },
            new() { MonthlyAvgSpend = 60000, TransactionFrequency = 45, TopCategoryPercentage = 90, CategoryCount = 1, Label = 2 },
            new() { MonthlyAvgSpend = 35000, TransactionFrequency = 50, TopCategoryPercentage = 70, CategoryCount = 2, Label = 2 },
            new() { MonthlyAvgSpend = 80000, TransactionFrequency = 38, TopCategoryPercentage = 88, CategoryCount = 1, Label = 2 },
            new() { MonthlyAvgSpend = 45000, TransactionFrequency = 42, TopCategoryPercentage = 78, CategoryCount = 2, Label = 2 },
            new() { MonthlyAvgSpend = 55000, TransactionFrequency = 33, TopCategoryPercentage = 82, CategoryCount = 1, Label = 2 },
        };
    }
}