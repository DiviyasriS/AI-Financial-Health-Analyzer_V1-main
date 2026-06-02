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
    /// Produces an explainable financial risk prediction from a pre-computed feature vector.
    ///
    /// Domain rule:
    /// - RiskLevel may use the trained model when available.
    /// - RiskScore is always the transparent financial risk-severity scorecard result.
    ///   It is not the ML confidence probability.
    /// </summary>
    /// <param name="features">Features extracted by <see cref="FinancialFeatureExtractor"/>.</param>
    /// <returns>Risk level string ("Low" | "Medium" | "High" | "Unknown") and a 0-1 severity score.</returns>
    public (string RiskLevel, float RiskScore) Predict(UserRiskFeatures features)
    {
        RiskAssessmentResult assessment = RiskLabelGenerator.GenerateAssessment(features);

        if (assessment.RiskLevel == "Unknown")
        {
            return (assessment.RiskLevel, 0f);
        }

        string finalRiskLevel = assessment.RiskLevel;

        if (_isModelLoaded && _predictionPool != null)
        {
            RiskInput input = BuildInput(features);
            PredictionEngine<RiskInput, RiskOutput> engine = _predictionPool.Get();

            try
            {
                RiskOutput result = engine.Predict(input);
                string mlRiskLevel = ToRiskLevel(result.PredictedLabel);

                // Guardrail: never allow the model to jump more than one level away
                // from the explainable scorecard. This keeps the result trustworthy
                // while still allowing ML to refine borderline cases.
                finalRiskLevel = ReconcileRiskLevels(assessment.RiskLevel, mlRiskLevel);

                _logger.LogInformation(
                    "Risk prediction: FinalLevel={FinalLevel}, ScorecardLevel={ScorecardLevel}, MlLevel={MlLevel}, RiskSeverity={RiskSeverity:P1}, RawMlScores=[{Scores}]",
                    finalRiskLevel,
                    assessment.RiskLevel,
                    mlRiskLevel,
                    assessment.RiskScorePercent / 100f,
                    result.Score.Length > 0
                        ? string.Join(", ", result.Score.Select(s => s.ToString("F3")))
                        : "none");
            }
            finally
            {
                _predictionPool.Return(engine);
            }
        }
        else
        {
            _logger.LogWarning(
                "RiskPredictionService: model not loaded; using explainable rule-based risk assessment.");
        }

        return (finalRiskLevel, Math.Clamp(assessment.RiskScorePercent / 100f, 0f, 1f));
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

    private static string ToRiskLevel(float predictedLabel) => predictedLabel switch
    {
        var l when Math.Abs(l - LabelLow) < 0.01f => "Low",
        var l when Math.Abs(l - LabelMedium) < 0.01f => "Medium",
        var l when Math.Abs(l - LabelHigh) < 0.01f => "High",
        _ => "Low"
    };

    private static string ReconcileRiskLevels(string scorecardLevel, string mlLevel)
    {
        int scorecardRank = RiskRank(scorecardLevel);
        int mlRank = RiskRank(mlLevel);

        if (scorecardRank == 0 || mlRank == 0)
            return scorecardLevel;

        if (Math.Abs(scorecardRank - mlRank) <= 1)
            return mlLevel;

        return scorecardLevel;
    }

    private static int RiskRank(string level) => level switch
    {
        "Low" => 1,
        "Medium" => 2,
        "High" => 3,
        _ => 0
    };

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