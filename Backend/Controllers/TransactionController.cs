using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Backend.Models.ML;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TransactionController : ControllerBase
{
    private readonly ITransactionService _transactionService;
    private readonly ITransactionRepository _transactionRepository;
    private readonly IRiskPredictionRepository _riskRepo;
    private readonly RiskPredictionService _riskPredictionService;
    private readonly AlertService _alertService;
    private readonly ILogger<TransactionController> _logger;

    private static readonly string[] AllowedExtensions = { ".csv", ".xlsx", ".xls", ".pdf" };

    public TransactionController(
        ITransactionService transactionService,
        ITransactionRepository transactionRepository,
        IRiskPredictionRepository riskRepo,
        RiskPredictionService riskPredictionService,
        AlertService alertService,
        ILogger<TransactionController> logger)
    {
        _transactionService = transactionService;
        _transactionRepository = transactionRepository;
        _riskRepo = riskRepo;
        _riskPredictionService = riskPredictionService;
        _alertService = alertService;
        _logger = logger;
    }

    [HttpPost("upload")]
    public async Task<IActionResult> UploadFile(IFormFile file)
    {
        if (!TryGetUserIdFromToken(out int userId))
            return Unauthorized(new { message = "Invalid token." });

        if (file == null || file.Length == 0)
            return BadRequest(new { message = "Please upload a valid file." });

        if (file.Length > 10 * 1024 * 1024)
            return BadRequest(new { message = "File size must be under 10MB." });

        string extension = Path.GetExtension(file.FileName).ToLowerInvariant().Trim();

        if (!AllowedExtensions.Contains(extension))
        {
            return BadRequest(new
            {
                message = $"Unsupported file type '{extension}'. Supported: CSV, XLSX, XLS, PDF."
            });
        }

        using Stream stream = file.OpenReadStream();

        FileProcessingResultDto result = await _transactionService.ProcessAndSaveAsync(
            stream,
            file.FileName,
            userId);

        await TryGenerateRiskAndSendAlertAsync(userId);

        return Ok(result);
    }

    [HttpGet]
    public async Task<IActionResult> GetTransactions()
    {
        if (!TryGetUserIdFromToken(out int userId))
            return Unauthorized(new { message = "Invalid token." });

        List<TransactionDto> transactions = await _transactionService.GetTransactionsAsync(userId);
        return Ok(transactions);
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary()
    {
        if (!TryGetUserIdFromToken(out int userId))
            return Unauthorized(new { message = "Invalid token." });

        SpendingSummaryDto summary = await _transactionService.GetSummaryAsync(userId);
        return Ok(summary);
    }

    [HttpDelete]
    public async Task<IActionResult> DeleteAllTransactions()
    {
        if (!TryGetUserIdFromToken(out int userId))
            return Unauthorized(new { message = "Invalid token." });

        int deletedCount = await _transactionService.DeleteAllTransactionsAsync(userId);

        if (deletedCount == 0)
        {
            return BadRequest(new
            {
                message = "No transactions found to delete."
            });
        }

        return Ok(new
        {
            deletedCount,
            message = $"Successfully deleted {deletedCount} transactions. You can now re-upload your bank statement."
        });
    }

    private async Task TryGenerateRiskAndSendAlertAsync(int userId)
    {
        try
        {
            SpendingSummaryDto summary = await _transactionService.GetSummaryAsync(userId);

            if (summary.TotalTransactions == 0)
                return;

            List<Transaction> transactions = await _transactionRepository.GetByUserIdAsync(userId);
            UserRiskFeatures features = FinancialFeatureExtractor.Extract(transactions);

            RiskAssessmentResult assessment = RiskLabelGenerator.GenerateAssessment(features);
            (string riskLevel, float riskScore) = _riskPredictionService.Predict(features);

            RiskPrediction prediction = new()
            {
                UserId = userId,
                RiskScore = riskScore,
                RiskLevel = riskLevel,
                MonthlyAvgSpend = summary.AverageMonthlySpend,
                TotalTransactions = summary.TotalTransactions,
                CategoryCount = summary.CategoryBreakdown.Count,
                TopCategory = features.TopCategory,
                TopCategoryPercentage = (decimal)features.TopCategoryPercentage,
                FoodSpendPercentage = (decimal)features.FoodSpendPercentage,
                EntertainmentSpendPercentage = (decimal)features.EntertainmentSpendPercentage,
                MoMSpendChangePercentage = (decimal)features.MoMSpendChangePercentage,
                PredictedAt = DateTime.UtcNow
            };

            await _riskRepo.SaveAsync(prediction);

            RiskDto riskDto = new()
            {
                RiskLevel = riskLevel,
                RiskScore = riskScore,
                PredictedAt = prediction.PredictedAt,
                Description = assessment.Summary,
                RiskFactors = assessment.RiskFactors,
                PositiveSignals = assessment.PositiveSignals
            };

            await _alertService.SendRiskAlertAsync(userId, riskDto);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Upload succeeded, but risk alert generation failed. UserId={UserId}",
                userId);
        }
    }

    private bool TryGetUserIdFromToken(out int userId)
    {
        userId = 0;

        Claim? claim = User.FindFirst(ClaimTypes.NameIdentifier)
                    ?? User.FindFirst("userId");

        return claim != null && int.TryParse(claim.Value, out userId);
    }
}