using Backend.Models.ML;
using Microsoft.ML;
using Microsoft.ML.Data;


/// <summary>
/// Responsible for training the financial risk classification model.
/// 
/// Training pipeline:
/// 1. Pull all transactions grouped by user from the repository.
/// 2. For each user, run <see cref="FinancialFeatureExtractor"/> to compute features.
/// 3. Run <see cref="RiskLabelGenerator"/> to assign a risk label.
/// 4. If fewer than <see cref="MinUsersForRealTraining"/> real samples exist,
///    augment with synthetic samples generated from representative profiles.
/// 5. Train a FSdcaMaximumEntropy.
/// 6. Evaluate on a held-out split and log key metrics.
/// 7. Save the model to <see cref="ModelPath"/> as a .zip file.
/// </summary>
public class RiskModelTrainer
{
    // ── Configuration ────────────────────────────────────────────────────────
    public static readonly string ModelPath =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Models", "risk_model.zip");

    private const int MinUsersForRealTraining = 10;

    private readonly ITransactionRepository _transactionRepository;
    private readonly ILogger<RiskModelTrainer> _logger;
    private readonly MLContext _mlContext;

    public RiskModelTrainer(
        ITransactionRepository transactionRepository,
        ILogger<RiskModelTrainer> logger)
    {
        _transactionRepository = transactionRepository;
        _logger                = logger;
        _mlContext             = new MLContext(seed: 42);
    }

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Trains (or re-trains) the model using real + synthetic data and saves it.
    /// Returns the trained <see cref="ITransformer"/> for immediate use.
    /// </summary>
    public async Task<ITransformer> TrainAndSaveAsync()
    {
        _logger.LogInformation("Starting risk model training...");

        List<RiskInput> allSamples = await BuildTrainingSamplesAsync();

        _logger.LogInformation(
            "Training on {Count} samples ({Labels})",
            allSamples.Count,
            FormatLabelDistribution(allSamples));

        IDataView dataView = _mlContext.Data.LoadFromEnumerable(allSamples);

        DataOperationsCatalog.TrainTestData split =
            _mlContext.Data.TrainTestSplit(dataView, testFraction: 0.2, seed: 42);

        IEstimator<ITransformer> pipeline = BuildPipeline();
        ITransformer trainedModel         = pipeline.Fit(split.TrainSet);

        EvaluateModel(trainedModel, split.TestSet);
        SaveModel(trainedModel, dataView.Schema);

        return trainedModel;
    }

    /// <summary>
    /// Loads a previously saved model from disk.
    /// Throws <see cref="FileNotFoundException"/> if the model file does not exist.
    /// </summary>
    public ITransformer LoadSavedModel()
    {
        if (!File.Exists(ModelPath))
            throw new FileNotFoundException($"Model file not found at {ModelPath}. Run training first.");

        _logger.LogInformation("Loading saved model from {Path}", ModelPath);
        return _mlContext.Model.Load(ModelPath, out _);
    }

    public MLContext MlContext => _mlContext;

    // ── Private helpers ──────────────────────────────────────────────────────

    private async Task<List<RiskInput>> BuildTrainingSamplesAsync()
    {
        // ── Gather all transactions per user ─────────────────────────────────
        // We call GetByUserIdAsync for each known user.
        // In a production scenario you would have a dedicated repo method;
        // here we extract unique userIds from the returned transactions.
        List<RiskInput> samples = new();

        // Use ITransactionRepository to get all transactions (no userId filter)
        // We need all user ids — derive them from all transactions in the system.
        List<int> userIds = await _transactionRepository.GetAllUserIdsAsync();

        foreach (int userId in userIds)
        {
            List<Transaction> transactions =
                await _transactionRepository.GetByUserIdAsync(userId);

            if (transactions.Count < 3) continue; // too few to be meaningful

            UserRiskFeatures features = FinancialFeatureExtractor.Extract(transactions);
            float label               = RiskLabelGenerator.GenerateLabel(features);

            samples.Add(FeaturesToInput(features, label));
        }

        _logger.LogInformation("Extracted {Count} real user samples", samples.Count);

        // ── Augment with synthetic profiles if needed ─────────────────────────
        List<RiskInput> synthetic = GenerateSyntheticSamples();
        samples.AddRange(synthetic);

        _logger.LogInformation(
            "Added {Count} synthetic samples (total: {Total})",
            synthetic.Count, samples.Count);

        return samples;
    }

    private IEstimator<ITransformer> BuildPipeline()
    {
        string[] featureColumns = new[]
        {
            nameof(RiskInput.MonthlyAvgSpend),
            nameof(RiskInput.MonthlySpendStdDev),
            nameof(RiskInput.TransactionFrequency),
            nameof(RiskInput.LargeTransactionFrequency),
            nameof(RiskInput.TopCategoryPercentage),
            nameof(RiskInput.CategoryCount),
            nameof(RiskInput.EssentialSpendPercentage),
            nameof(RiskInput.FoodSpendPercentage),
            nameof(RiskInput.EntertainmentSpendPercentage),
            nameof(RiskInput.MoMSpendChangePercentage),
            nameof(RiskInput.SpendingTrend)
        };

        // SdcaMaximumEntropy
        // interactions better than SDCA for tabular financial data.
        // It also produces feature importance scores for explainability.
        return _mlContext.Transforms
    .Concatenate("Features", featureColumns)
    .Append(_mlContext.Transforms.Conversion.MapValueToKey(
        outputColumnName: "Label",
        inputColumnName:  "Label",
        keyOrdinality: Microsoft.ML.Transforms.ValueToKeyMappingEstimator.KeyOrdinality.ByValue))
    .Append(_mlContext.MulticlassClassification.Trainers.SdcaMaximumEntropy(
        labelColumnName:   "Label",
        featureColumnName: "Features",
        maximumNumberOfIterations: 200))
    .Append(_mlContext.Transforms.Conversion.MapKeyToValue(
        outputColumnName: "PredictedLabel",
        inputColumnName:  "PredictedLabel"));
    }

    private void EvaluateModel(ITransformer model, IDataView testSet)
    {
        IDataView predictions = model.Transform(testSet);

        MulticlassClassificationMetrics metrics =
            _mlContext.MulticlassClassification.Evaluate(
                predictions,
                labelColumnName:          "Label",
                predictedLabelColumnName: "PredictedLabel");

        _logger.LogInformation(
            "Model evaluation — MicroAccuracy: {Micro:P2}, MacroAccuracy: {Macro:P2}, " +
            "LogLoss: {LogLoss:F4}, LogLossReduction: {LLR:F4}",
            metrics.MicroAccuracy,
            metrics.MacroAccuracy,
            metrics.LogLoss,
            metrics.LogLossReduction);

        if (metrics.MicroAccuracy < 0.60)
        {
            _logger.LogWarning(
                "Model micro-accuracy {Acc:P0} is below 60%. " +
                "Consider collecting more real user data before deploying.",
                metrics.MicroAccuracy);
        }
    }

    private void SaveModel(ITransformer model, DataViewSchema schema)
    {
        string? directory = Path.GetDirectoryName(ModelPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        _mlContext.Model.Save(model, schema, ModelPath);
        _logger.LogInformation("Model saved to {Path}", ModelPath);
    }

    private static RiskInput FeaturesToInput(UserRiskFeatures f, float label) =>
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
            Label                        = label
        };

    private static string FormatLabelDistribution(List<RiskInput> samples)
    {
        int low    = samples.Count(s => Math.Abs(s.Label - RiskLabelGenerator.LabelLow)    < 0.01f);
        int medium = samples.Count(s => Math.Abs(s.Label - RiskLabelGenerator.LabelMedium) < 0.01f);
        int high   = samples.Count(s => Math.Abs(s.Label - RiskLabelGenerator.LabelHigh)   < 0.01f);
        return $"Low={low}, Medium={medium}, High={high}";
    }

    // ── Synthetic sample generation ──────────────────────────────────────────
    // Synthetic samples represent archetypal spending profiles.
    // They are always included to ensure label coverage even when real data is sparse.
    // As real users accumulate, these samples become proportionally less influential.

    private static List<RiskInput> GenerateSyntheticSamples()
    {
        return new List<RiskInput>
        {
            // ── LOW risk profiles ────────────────────────────────────────────
            // Young professional: moderate spend, good category spread
            S(8_000,  800,  8,  0.5f, 25, 5, 40, 20, 10,  5,  0.05f, RiskLabelGenerator.LabelLow),
            S(12_000, 1_000, 10, 0.8f, 30, 6, 45, 22, 8, -5, -0.02f, RiskLabelGenerator.LabelLow),
            S(15_000, 2_000, 12, 1.0f, 35, 5, 38, 18, 9, 10,  0.10f, RiskLabelGenerator.LabelLow),
            S(9_500,  900,  9,  0.6f, 28, 6, 42, 15, 7,  2,  0.01f, RiskLabelGenerator.LabelLow),
            S(10_000, 1_100, 11, 0.7f, 32, 5, 44, 21, 8, -2, -0.01f, RiskLabelGenerator.LabelLow),
            S(7_000,  600,  7,  0.4f, 22, 7, 48, 17, 6,  8,  0.06f, RiskLabelGenerator.LabelLow),
            S(13_000, 1_300, 11, 0.9f, 27, 6, 41, 19, 9,  3,  0.02f, RiskLabelGenerator.LabelLow),
            S(6_500,  500,  6,  0.3f, 20, 7, 50, 16, 5,  0,  0.00f, RiskLabelGenerator.LabelLow),
            S(11_000, 950,  10, 0.7f, 33, 5, 43, 20, 8, -8, -0.05f, RiskLabelGenerator.LabelLow),
            S(14_000, 1_500, 13, 1.1f, 29, 6, 40, 23, 9, 12,  0.07f, RiskLabelGenerator.LabelLow),
            S(5_500,  400,  5,  0.2f, 18, 8, 52, 14, 4,  1,  0.00f, RiskLabelGenerator.LabelLow),
            S(16_000, 1_800, 14, 1.2f, 38, 5, 37, 19, 10, 15, 0.08f, RiskLabelGenerator.LabelLow),

            // ── MEDIUM risk profiles ─────────────────────────────────────────
            // Higher spend, some concentration, moderate variance
            S(22_000, 4_000, 18, 2.0f, 52, 4, 30, 30, 14, 18,  0.15f, RiskLabelGenerator.LabelMedium),
            S(28_000, 5_000, 20, 2.5f, 55, 3, 28, 35, 16, 22,  0.20f, RiskLabelGenerator.LabelMedium),
            S(18_000, 3_500, 16, 1.8f, 58, 3, 25, 28, 20, 25,  0.25f, RiskLabelGenerator.LabelMedium),
            S(25_000, 6_000, 22, 3.0f, 60, 3, 22, 32, 18, 30,  0.30f, RiskLabelGenerator.LabelMedium),
            S(20_000, 5_500, 19, 2.2f, 54, 4, 27, 31, 15, 20,  0.18f, RiskLabelGenerator.LabelMedium),
            S(30_000, 7_000, 25, 3.5f, 48, 3, 32, 36, 12, 28,  0.28f, RiskLabelGenerator.LabelMedium),
            S(17_000, 3_000, 15, 1.6f, 50, 4, 33, 27, 17, 15,  0.12f, RiskLabelGenerator.LabelMedium),
            S(23_000, 4_500, 20, 2.3f, 56, 3, 26, 33, 19, 24,  0.22f, RiskLabelGenerator.LabelMedium),
            S(26_000, 6_500, 21, 2.8f, 62, 2, 20, 34, 22, 35,  0.35f, RiskLabelGenerator.LabelMedium),
            S(19_000, 3_800, 17, 1.9f, 53, 4, 29, 29, 16, 19,  0.16f, RiskLabelGenerator.LabelMedium),
            S(32_000, 8_000, 26, 3.8f, 58, 3, 24, 37, 13, 32,  0.30f, RiskLabelGenerator.LabelMedium),
            S(21_000, 4_200, 18, 2.1f, 51, 4, 31, 30, 14, 21,  0.19f, RiskLabelGenerator.LabelMedium),

            // ── HIGH risk profiles ───────────────────────────────────────────
            // Very high spend, concentrated categories, high variance, worsening trend
            S(50_000, 15_000, 35, 6.0f, 75, 2, 10, 45, 30, 40,  0.50f, RiskLabelGenerator.LabelHigh),
            S(70_000, 20_000, 42, 8.0f, 82, 1, 8,  50, 35, 50,  0.60f, RiskLabelGenerator.LabelHigh),
            S(45_000, 18_000, 38, 7.0f, 78, 2, 12, 48, 28, 45,  0.55f, RiskLabelGenerator.LabelHigh),
            S(60_000, 22_000, 40, 7.5f, 80, 1, 9,  52, 32, 55,  0.65f, RiskLabelGenerator.LabelHigh),
            S(55_000, 16_000, 36, 6.5f, 76, 2, 11, 46, 33, 42,  0.52f, RiskLabelGenerator.LabelHigh),
            S(80_000, 25_000, 45, 9.0f, 85, 1, 7,  55, 36, 60,  0.70f, RiskLabelGenerator.LabelHigh),
            S(48_000, 17_000, 37, 7.2f, 79, 2, 10, 49, 29, 48,  0.58f, RiskLabelGenerator.LabelHigh),
            S(65_000, 21_000, 41, 8.2f, 83, 1, 8,  53, 34, 52,  0.62f, RiskLabelGenerator.LabelHigh),
            S(42_000, 14_000, 33, 5.8f, 72, 2, 13, 44, 27, 38,  0.45f, RiskLabelGenerator.LabelHigh),
            S(75_000, 23_000, 44, 8.8f, 84, 1, 7,  54, 37, 58,  0.68f, RiskLabelGenerator.LabelHigh),
            S(52_000, 19_000, 39, 7.8f, 81, 2, 9,  51, 31, 46,  0.56f, RiskLabelGenerator.LabelHigh),
            S(38_000, 12_000, 30, 5.0f, 70, 2, 15, 42, 25, 35,  0.40f, RiskLabelGenerator.LabelHigh),
        };
    }

    /// <summary>Shorthand factory for synthetic <see cref="RiskInput"/> rows.</summary>
    private static RiskInput S(
        float monthlyAvg, float stdDev, float txnFreq, float largeTxnFreq,
        float topCatPct,  float catCount,
        float essentialPct, float foodPct, float entertainPct,
        float momChange, float trend, float label) =>
        new()
        {
            MonthlyAvgSpend              = monthlyAvg,
            MonthlySpendStdDev           = stdDev,
            TransactionFrequency         = txnFreq,
            LargeTransactionFrequency    = largeTxnFreq,
            TopCategoryPercentage        = topCatPct,
            CategoryCount                = catCount,
            EssentialSpendPercentage     = essentialPct,
            FoodSpendPercentage          = foodPct,
            EntertainmentSpendPercentage = entertainPct,
            MoMSpendChangePercentage     = momChange,
            SpendingTrend                = trend,
            Label                        = label
        };
}
