using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using Backend.Models.ML;

// RiskPredictionService encapsulates all ML.NET logic.
//
// KEY DESIGN DECISIONS:
// 1. Registered as SINGLETON — model trains once at startup
// 2. Uses PredictionEnginePool for thread safety
// 3. Uses softmax probabilities instead of raw scores
// 4. Explicit label schema prevents key mapping ambiguity

public class RiskPredictionService
{
    private readonly MLContext _mlContext;
    private readonly ILogger<RiskPredictionService> _logger;
    private PredictionEnginePool<RiskInput, RiskOutput>? _predictionEnginePool;
    private readonly object _initLock = new();
    private bool _isModelTrained = false;

    // Label constants — must match training data Label values
    private const float LABEL_LOW    = 0f;
    private const float LABEL_MEDIUM = 1f;
    private const float LABEL_HIGH   = 2f;

    public RiskPredictionService(ILogger<RiskPredictionService> logger)
    {
        _mlContext = new MLContext(seed: 42);
        _logger    = logger;
    }

    // Called once at application startup via IHostedService or lazy on first use
    public void EnsureModelTrained()
    {
        if (_isModelTrained) return;

        lock (_initLock)
        {
            if (_isModelTrained) return;
            TrainModel();
            _isModelTrained = true;
        }
    }

    private void TrainModel()
    {
        _logger.LogInformation("Training ML.NET risk prediction model...");

        List<RiskInput> trainingData = GenerateSyntheticTrainingData();
        IDataView dataView = _mlContext.Data.LoadFromEnumerable(trainingData);

        // Split for basic validation logging (not used for actual training)
        DataOperationsCatalog.TrainTestData split =
            _mlContext.Data.TrainTestSplit(dataView, testFraction: 0.2);

        // Pipeline:
        // 1. Normalize features — SDCA benefits from normalized inputs
        // 2. Map float Label → key (required for multiclass)
        // 3. Train SDCA with max entropy (gives calibrated per-class scores)
        // 4. Map predicted key back to original float label
        var pipeline = _mlContext.Transforms
            .NormalizeMinMax("MonthlyAvgSpend")
            .Append(_mlContext.Transforms.NormalizeMinMax("TransactionFrequency"))
            .Append(_mlContext.Transforms.NormalizeMinMax("TopCategoryPercentage"))
            .Append(_mlContext.Transforms.NormalizeMinMax("CategoryCount"))
            .Append(_mlContext.Transforms.Concatenate("Features",
                "MonthlyAvgSpend",
                "TransactionFrequency",
                "TopCategoryPercentage",
                "CategoryCount"))
            .Append(_mlContext.Transforms.Conversion.MapValueToKey(
                outputColumnName: "Label",
                inputColumnName: "Label",
                keyOrdinality: Microsoft.ML.Transforms.ValueToKeyMappingEstimator.KeyOrdinality.ByValue))
            .Append(_mlContext.MulticlassClassification.Trainers.SdcaMaximumEntropy(
                labelColumnName: "Label",
                featureColumnName: "Features",
                maximumNumberOfIterations: 100))
            .Append(_mlContext.Transforms.Conversion.MapKeyToValue(
                outputColumnName: "PredictedLabel",
                inputColumnName: "PredictedLabel"));

        ITransformer trainedModel = pipeline.Fit(split.TrainSet);

        // Log micro-accuracy on test set for visibility
        IDataView predictions = trainedModel.Transform(split.TestSet);
        MulticlassClassificationMetrics metrics =
            _mlContext.MulticlassClassification.Evaluate(predictions,
                labelColumnName: "Label",
                predictedLabelColumnName: "PredictedLabel");

        _logger.LogInformation(
            "Model training complete. MicroAccuracy={Accuracy:P2}, MacroAccuracy={MacroAccuracy:P2}",
            metrics.MicroAccuracy,
            metrics.MacroAccuracy);

        // Create thread-safe pool instead of single engine
        _predictionEnginePool = new PredictionEnginePool<RiskInput, RiskOutput>(
            _mlContext, trainedModel);

        _logger.LogInformation("PredictionEnginePool initialized.");
    }

public (string riskLevel, float riskScore) Predict(
    decimal monthlyAvgSpend,
    int totalTransactions,
    int monthCount,
    float topCategoryPercentage,
    int categoryCount)
{
    EnsureModelTrained();

    if (_predictionEnginePool == null)
    {
        _logger.LogWarning(
            "Prediction pool not available. Returning default Low risk.");

        return ("Low", 0.1f);
    }

    float transactionFrequency = monthCount > 0
        ? (float)totalTransactions / monthCount
        : totalTransactions;

    var input = new RiskInput
    {
        MonthlyAvgSpend = (float)monthlyAvgSpend,
        TransactionFrequency = transactionFrequency,
        TopCategoryPercentage = topCategoryPercentage,
        CategoryCount = categoryCount
    };

    PredictionEngine<RiskInput, RiskOutput> engine =
        _predictionEnginePool.GetPredictionEngine();

    try
    {
        RiskOutput result = engine.Predict(input);

        string riskLevel = result.PredictedLabel switch
        {
            var l when Math.Abs(l - LABEL_LOW) < 0.01f => "Low",
            var l when Math.Abs(l - LABEL_MEDIUM) < 0.01f => "Medium",
            var l when Math.Abs(l - LABEL_HIGH) < 0.01f => "High",
            _ => "Low"
        };

        float riskScore = riskLevel switch
        {
            "Low" => result.Score.Length > 0
                ? result.Score[0]
                : 0.1f,

            "Medium" => result.Score.Length > 1
                ? result.Score[1]
                : 0.5f,

            "High" => result.Score.Length > 2
                ? result.Score[2]
                : 0.9f,

            _ => 0.5f
        };

        riskScore = Math.Clamp(riskScore, 0f, 1f);

        _logger.LogInformation(
            "Risk prediction: Level={Level}, Score={Score:P1}, RawScores=[{Scores}]",
            riskLevel,
            riskScore,
            result.Score.Length > 0
                ? string.Join(", ",
                    result.Score.Select(s => s.ToString("F3")))
                : "none");

        return (riskLevel, riskScore);
    }
    finally
    {
        _predictionEnginePool.ReturnEngine(engine);
    }
}
    private List<RiskInput> GenerateSyntheticTrainingData()
    {
        // IMPROVED: More samples, overlapping ranges, better representation
        // Labels: 0=Low, 1=Medium, 2=High
        // Features: MonthlyAvgSpend, TransactionFrequency, TopCategoryPercentage, CategoryCount
        //
        // LOW risk profile:  low-moderate spend, spread across 4-6 categories,
        //                    no single dominant category (< 45%)
        // MEDIUM risk:       moderate-high spend OR one category > 50%,
        //                    OR high transaction frequency
        // HIGH risk:         very high spend AND concentrated spending AND high frequency

        return new List<RiskInput>
        {
            // ── LOW risk (Label = 0) — 16 samples ────────────────────────
            new() { MonthlyAvgSpend = 3000,  TransactionFrequency = 5,  TopCategoryPercentage = 25, CategoryCount = 4, Label = LABEL_LOW },
            new() { MonthlyAvgSpend = 4500,  TransactionFrequency = 7,  TopCategoryPercentage = 30, CategoryCount = 5, Label = LABEL_LOW },
            new() { MonthlyAvgSpend = 5000,  TransactionFrequency = 8,  TopCategoryPercentage = 28, CategoryCount = 4, Label = LABEL_LOW },
            new() { MonthlyAvgSpend = 6000,  TransactionFrequency = 9,  TopCategoryPercentage = 35, CategoryCount = 5, Label = LABEL_LOW },
            new() { MonthlyAvgSpend = 7000,  TransactionFrequency = 10, TopCategoryPercentage = 38, CategoryCount = 5, Label = LABEL_LOW },
            new() { MonthlyAvgSpend = 8000,  TransactionFrequency = 11, TopCategoryPercentage = 40, CategoryCount = 6, Label = LABEL_LOW },
            new() { MonthlyAvgSpend = 9000,  TransactionFrequency = 12, TopCategoryPercentage = 42, CategoryCount = 6, Label = LABEL_LOW },
            new() { MonthlyAvgSpend = 10000, TransactionFrequency = 10, TopCategoryPercentage = 35, CategoryCount = 6, Label = LABEL_LOW },
            new() { MonthlyAvgSpend = 3500,  TransactionFrequency = 6,  TopCategoryPercentage = 22, CategoryCount = 4, Label = LABEL_LOW },
            new() { MonthlyAvgSpend = 5500,  TransactionFrequency = 8,  TopCategoryPercentage = 32, CategoryCount = 5, Label = LABEL_LOW },
            new() { MonthlyAvgSpend = 7500,  TransactionFrequency = 10, TopCategoryPercentage = 36, CategoryCount = 5, Label = LABEL_LOW },
            new() { MonthlyAvgSpend = 4000,  TransactionFrequency = 7,  TopCategoryPercentage = 27, CategoryCount = 4, Label = LABEL_LOW },
            new() { MonthlyAvgSpend = 6500,  TransactionFrequency = 9,  TopCategoryPercentage = 33, CategoryCount = 5, Label = LABEL_LOW },
            new() { MonthlyAvgSpend = 8500,  TransactionFrequency = 11, TopCategoryPercentage = 39, CategoryCount = 6, Label = LABEL_LOW },
            new() { MonthlyAvgSpend = 9500,  TransactionFrequency = 12, TopCategoryPercentage = 41, CategoryCount = 6, Label = LABEL_LOW },
            new() { MonthlyAvgSpend = 2500,  TransactionFrequency = 4,  TopCategoryPercentage = 20, CategoryCount = 3, Label = LABEL_LOW },

            // ── MEDIUM risk (Label = 1) — 16 samples ─────────────────────
            new() { MonthlyAvgSpend = 12000, TransactionFrequency = 15, TopCategoryPercentage = 52, CategoryCount = 3, Label = LABEL_MEDIUM },
            new() { MonthlyAvgSpend = 15000, TransactionFrequency = 18, TopCategoryPercentage = 55, CategoryCount = 3, Label = LABEL_MEDIUM },
            new() { MonthlyAvgSpend = 18000, TransactionFrequency = 20, TopCategoryPercentage = 50, CategoryCount = 4, Label = LABEL_MEDIUM },
            new() { MonthlyAvgSpend = 20000, TransactionFrequency = 22, TopCategoryPercentage = 58, CategoryCount = 3, Label = LABEL_MEDIUM },
            new() { MonthlyAvgSpend = 22000, TransactionFrequency = 14, TopCategoryPercentage = 48, CategoryCount = 4, Label = LABEL_MEDIUM },
            new() { MonthlyAvgSpend = 25000, TransactionFrequency = 25, TopCategoryPercentage = 60, CategoryCount = 3, Label = LABEL_MEDIUM },
            new() { MonthlyAvgSpend = 10000, TransactionFrequency = 28, TopCategoryPercentage = 65, CategoryCount = 2, Label = LABEL_MEDIUM },
            new() { MonthlyAvgSpend = 16000, TransactionFrequency = 17, TopCategoryPercentage = 53, CategoryCount = 3, Label = LABEL_MEDIUM },
            new() { MonthlyAvgSpend = 13000, TransactionFrequency = 16, TopCategoryPercentage = 51, CategoryCount = 3, Label = LABEL_MEDIUM },
            new() { MonthlyAvgSpend = 19000, TransactionFrequency = 21, TopCategoryPercentage = 56, CategoryCount = 3, Label = LABEL_MEDIUM },
            new() { MonthlyAvgSpend = 11000, TransactionFrequency = 20, TopCategoryPercentage = 62, CategoryCount = 2, Label = LABEL_MEDIUM },
            new() { MonthlyAvgSpend = 23000, TransactionFrequency = 19, TopCategoryPercentage = 54, CategoryCount = 3, Label = LABEL_MEDIUM },
            new() { MonthlyAvgSpend = 17000, TransactionFrequency = 23, TopCategoryPercentage = 59, CategoryCount = 3, Label = LABEL_MEDIUM },
            new() { MonthlyAvgSpend = 14000, TransactionFrequency = 15, TopCategoryPercentage = 57, CategoryCount = 3, Label = LABEL_MEDIUM },
            new() { MonthlyAvgSpend = 24000, TransactionFrequency = 24, TopCategoryPercentage = 63, CategoryCount = 2, Label = LABEL_MEDIUM },
            new() { MonthlyAvgSpend = 21000, TransactionFrequency = 13, TopCategoryPercentage = 49, CategoryCount = 4, Label = LABEL_MEDIUM },

            // ── HIGH risk (Label = 2) — 16 samples ───────────────────────
            new() { MonthlyAvgSpend = 35000, TransactionFrequency = 30, TopCategoryPercentage = 72, CategoryCount = 2, Label = LABEL_HIGH },
            new() { MonthlyAvgSpend = 40000, TransactionFrequency = 35, TopCategoryPercentage = 78, CategoryCount = 2, Label = LABEL_HIGH },
            new() { MonthlyAvgSpend = 45000, TransactionFrequency = 38, TopCategoryPercentage = 80, CategoryCount = 1, Label = LABEL_HIGH },
            new() { MonthlyAvgSpend = 50000, TransactionFrequency = 40, TopCategoryPercentage = 82, CategoryCount = 2, Label = LABEL_HIGH },
            new() { MonthlyAvgSpend = 55000, TransactionFrequency = 42, TopCategoryPercentage = 85, CategoryCount = 1, Label = LABEL_HIGH },
            new() { MonthlyAvgSpend = 60000, TransactionFrequency = 45, TopCategoryPercentage = 88, CategoryCount = 1, Label = LABEL_HIGH },
            new() { MonthlyAvgSpend = 70000, TransactionFrequency = 38, TopCategoryPercentage = 86, CategoryCount = 1, Label = LABEL_HIGH },
            new() { MonthlyAvgSpend = 75000, TransactionFrequency = 43, TopCategoryPercentage = 90, CategoryCount = 1, Label = LABEL_HIGH },
            new() { MonthlyAvgSpend = 38000, TransactionFrequency = 32, TopCategoryPercentage = 74, CategoryCount = 2, Label = LABEL_HIGH },
            new() { MonthlyAvgSpend = 42000, TransactionFrequency = 36, TopCategoryPercentage = 76, CategoryCount = 2, Label = LABEL_HIGH },
            new() { MonthlyAvgSpend = 48000, TransactionFrequency = 39, TopCategoryPercentage = 79, CategoryCount = 1, Label = LABEL_HIGH },
            new() { MonthlyAvgSpend = 52000, TransactionFrequency = 41, TopCategoryPercentage = 83, CategoryCount = 1, Label = LABEL_HIGH },
            new() { MonthlyAvgSpend = 58000, TransactionFrequency = 44, TopCategoryPercentage = 87, CategoryCount = 1, Label = LABEL_HIGH },
            new() { MonthlyAvgSpend = 65000, TransactionFrequency = 37, TopCategoryPercentage = 84, CategoryCount = 2, Label = LABEL_HIGH },
            new() { MonthlyAvgSpend = 80000, TransactionFrequency = 46, TopCategoryPercentage = 91, CategoryCount = 1, Label = LABEL_HIGH },
            new() { MonthlyAvgSpend = 30000, TransactionFrequency = 50, TopCategoryPercentage = 70, CategoryCount = 2, Label = LABEL_HIGH },
        };
    }
}

// Thread-safe prediction engine pool
public class PredictionEnginePool<TInput, TOutput>
    where TInput : class
    where TOutput : class, new()
{
    private readonly ObjectPool<PredictionEngine<TInput, TOutput>> _pool;

    public PredictionEnginePool(MLContext mlContext, ITransformer model)
    {
        var policy = new PredictionEnginePoolPolicy<TInput, TOutput>(
            mlContext,
            model);

        // FIXED: use DefaultObjectPool instead of ObjectPool.Create()
        _pool = new DefaultObjectPool<PredictionEngine<TInput, TOutput>>(policy);
    }

    public PredictionEngine<TInput, TOutput> GetPredictionEngine()
    {
        return _pool.Get();
    }

    public void ReturnEngine(PredictionEngine<TInput, TOutput> engine)
    {
        _pool.Return(engine);
    }
}

public class PredictionEnginePoolPolicy<TInput, TOutput>
    : IPooledObjectPolicy<PredictionEngine<TInput, TOutput>>
    where TInput : class
    where TOutput : class, new()
{
    private readonly MLContext _mlContext;
    private readonly ITransformer _model;

    public PredictionEnginePoolPolicy(
        MLContext mlContext,
        ITransformer model)
    {
        _mlContext = mlContext;
        _model = model;
    }

    public PredictionEngine<TInput, TOutput> Create()
    {
        return _mlContext.Model
            .CreatePredictionEngine<TInput, TOutput>(_model);
    }

    public bool Return(PredictionEngine<TInput, TOutput> obj)
    {
        return true;
    }
}