/// <summary>
/// Background service that trains or loads the ML risk model at application startup.
///
/// Startup sequence:
/// 1. If a saved model file exists, load it immediately so predictions are available fast.
/// 2. In the background, trigger a re-train to incorporate any new user data.
/// 3. After retraining, hot-swap the model in <see cref="RiskPredictionService"/>.
///
/// Retraining is also exposed via <see cref="TriggerRetrainAsync"/> so the admin
/// or a scheduled job can force a refresh.
/// </summary>
public class ModelTrainingHostedService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly RiskPredictionService _riskPredictionService;
    private readonly ILogger<ModelTrainingHostedService> _logger;

    public ModelTrainingHostedService(
        IServiceProvider serviceProvider,
        RiskPredictionService riskPredictionService,
        ILogger<ModelTrainingHostedService> logger)
    {
        _serviceProvider       = serviceProvider;
        _riskPredictionService = riskPredictionService;
        _logger                = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Fire-and-forget; we do not want to block the host from starting.
        _ = Task.Run(() => InitialiseModelAsync(cancellationToken), cancellationToken);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Triggers a full retrain using current transaction data.
    /// Safe to call at any time; uses a scoped repository.
    /// </summary>
    public async Task TriggerRetrainAsync()
    {
        _logger.LogInformation("Manual model retrain triggered.");
        await TrainModelAsync();
    }

    // ── Private ──────────────────────────────────────────────────────────────

    private async Task InitialiseModelAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Load existing model immediately if available
            if (File.Exists(RiskModelTrainer.ModelPath))
            {
                using IServiceScope scope = _serviceProvider.CreateScope();
                RiskModelTrainer trainer  = scope.ServiceProvider.GetRequiredService<RiskModelTrainer>();

                Microsoft.ML.ITransformer savedModel = trainer.LoadSavedModel();
                _riskPredictionService.SetModel(trainer.MlContext, savedModel);

                _logger.LogInformation(
                    "Loaded existing risk model from disk. Background retrain will follow.");
            }
            else
            {
                _logger.LogInformation(
                    "No saved model found. Training from scratch...");
            }

            // Always retrain in background to incorporate latest data
            if (!cancellationToken.IsCancellationRequested)
            {
                await TrainModelAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to initialise ML model. Predictions will use rule-based fallback.");
        }
    }

    private async Task TrainModelAsync()
    {
        try
        {
            using IServiceScope scope         = _serviceProvider.CreateScope();
            RiskModelTrainer trainer          = scope.ServiceProvider.GetRequiredService<RiskModelTrainer>();
            Microsoft.ML.ITransformer model   = await trainer.TrainAndSaveAsync();

            _riskPredictionService.SetModel(trainer.MlContext, model);
            _logger.LogInformation("Risk model training complete and hot-swapped.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Model training failed. Previous model (if any) remains active.");
        }
    }
}
