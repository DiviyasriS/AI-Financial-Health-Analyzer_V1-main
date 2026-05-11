using Backend.Models.ML;
using Microsoft.ML;
using Microsoft.Extensions.ObjectPool;

/// <summary>
/// Singleton service responsible for loading the trained risk model and producing
/// predictions with confidence scores.
///
/// Key design decisions:
/// - Registered as Singleton so the model is loaded once.
/// - Uses <see cref="ObjectPool{T}"/> of <see cref="PredictionEngine{TInput,TOutput}"/>
///   for thread-safe, high-throughput prediction without contention.
/// - Accepts <see cref="UserRiskFeatures"/> computed from real transactions.
/// - Falls back to graceful defaults if the model has not been trained yet.
/// </summary>
public class RiskPredictionService
{
    private readonly ILogger<RiskPredictionService> _logger;
    private readonly ObjectPoolProvider _poolProvider;

    private MLContext? _mlContext;
    private ITransformer? _trainedModel;
    private ObjectPool<PredictionEngine<RiskInput, RiskOutput>>? _predictionPool;

    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _isModelLoaded = false;

    // Label constants — must match those used during training
    private const float LabelLow    = 0f;
    private const float LabelMedium = 1f;
    private const float LabelHigh   = 2f;

    public RiskPredictionService(
        ILogger<RiskPredictionService> logger,
        ObjectPoolProvider poolProvider)
    {
        _logger       = logger;
        _poolProvider = poolProvider;
    }

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Ensures the model is ready. Called by the model-training hosted service
    /// after training completes, and also lazily on first prediction request.
    /// </summary>
    public void SetModel(MLContext mlContext, ITransformer model)
    {
        _mlContext     = mlContext;
        _trainedModel  = model;
        _predictionPool = BuildPool(mlContext, model);
        _isModelLoaded  = true;

        _logger.LogInformation(
            "RiskPredictionService: model loaded and prediction pool initialised.");
    }

    /// <summary>
    /// Produces a risk prediction from a pre-computed feature vector.
    /// </summary>
    /// <param name="features">Features extracted by <see cref="FinancialFeatureExtractor"/>.</param>
    /// <returns>Risk level string ("Low" | "Medium" | "High") and a 0-1 confidence score.</returns>
    public (string RiskLevel, float RiskScore) Predict(UserRiskFeatures features)
    {
        if (!_isModelLoaded || _predictionPool == null)
        {
            _logger.LogWarning(
                "RiskPredictionService: model not loaded, returning rule-based fallback.");
            return RuleBasedFallback(features);
        }

        RiskInput input = BuildInput(features);
        PredictionEngine<RiskInput, RiskOutput> engine = _predictionPool.Get();

        try
        {
            RiskOutput result = engine.Predict(input);

            string riskLevel = result.PredictedLabel switch
            {
                var l when Math.Abs(l - LabelLow)    < 0.01f => "Low",
                var l when Math.Abs(l - LabelMedium) < 0.01f => "Medium",
                var l when Math.Abs(l - LabelHigh)   < 0.01f => "High",
                _                                             => "Low"
            };

            // Score = confidence for the predicted class
            float riskScore = riskLevel switch
            {
                "Low"    => result.Score.Length > 0 ? result.Score[0] : 0.2f,
                "Medium" => result.Score.Length > 1 ? result.Score[1] : 0.5f,
                "High"   => result.Score.Length > 2 ? result.Score[2] : 0.9f,
                _        => 0.5f
            };

            riskScore = Math.Clamp(riskScore, 0f, 1f);

            _logger.LogInformation(
                "Risk prediction: Level={Level}, Score={Score:P1}, " +
                "RawScores=[{Scores}], MonthlyAvg={MonthlyAvg:F0}",
                riskLevel, riskScore,
                result.Score.Length > 0
                    ? string.Join(", ", result.Score.Select(s => s.ToString("F3")))
                    : "none",
                features.MonthlyAvgSpend);

            return (riskLevel, riskScore);
        }
        finally
        {
            _predictionPool.Return(engine);
        }
    }

    public bool IsModelLoaded => _isModelLoaded;

    // ── Private helpers ──────────────────────────────────────────────────────

    private static RiskInput BuildInput(UserRiskFeatures f) =>
        new()
        {
            MonthlyAvgSpend              = f.MonthlyAvgSpend,
            MonthlySpendStdDev           = f.MonthlySpendStdDev,
            TransactionFrequency         = f.TransactionFrequency,
            LargeTransactionFrequency    = f.LargeTransactionFrequency,
            TopCategoryPercentage        = f.TopCategoryPercentage,
            CategoryCount                = f.CategoryCount,
            EssentialSpendPercentage     = f.EssentialSpendPercentage,
            FoodSpendPercentage          = f.FoodSpendPercentage,
            EntertainmentSpendPercentage = f.EntertainmentSpendPercentage,
            MoMSpendChangePercentage     = f.MoMSpendChangePercentage,
            SpendingTrend                = f.SpendingTrend,
            Label                        = 0f // placeholder — not used during prediction
        };

    private ObjectPool<PredictionEngine<RiskInput, RiskOutput>> BuildPool(
        MLContext mlContext,
        ITransformer model)
    {
        IPooledObjectPolicy<PredictionEngine<RiskInput, RiskOutput>> policy =
            new PredictionEnginePolicy<RiskInput, RiskOutput>(mlContext, model);

        return _poolProvider.Create(policy);
    }

    /// <summary>
    /// Rule-based fallback used when the ML model is not yet available.
    /// Mirrors the <see cref="RiskLabelGenerator"/> logic to produce a consistent result.
    /// </summary>
    private static (string RiskLevel, float RiskScore) RuleBasedFallback(UserRiskFeatures features)
    {
        float label = RiskLabelGenerator.GenerateLabel(features);

        return label switch
        {
            RiskLabelGenerator.LabelHigh   => ("High",   0.85f),
            RiskLabelGenerator.LabelMedium => ("Medium", 0.55f),
            _                              => ("Low",    0.25f)
        };
    }
}

/// <summary>
/// Object pool policy for <see cref="PredictionEngine{TInput,TOutput}"/>.
/// Creating a PredictionEngine is expensive; pooling avoids per-request overhead.
/// </summary>
internal sealed class PredictionEnginePolicy<TInput, TOutput>
    : IPooledObjectPolicy<PredictionEngine<TInput, TOutput>>
    where TInput  : class
    where TOutput : class, new()
{
    private readonly MLContext _mlContext;
    private readonly ITransformer _model;

    internal PredictionEnginePolicy(MLContext mlContext, ITransformer model)
    {
        _mlContext = mlContext;
        _model     = model;
    }

    public PredictionEngine<TInput, TOutput> Create() =>
        _mlContext.Model.CreatePredictionEngine<TInput, TOutput>(_model);

    public bool Return(PredictionEngine<TInput, TOutput> obj) => true;
}