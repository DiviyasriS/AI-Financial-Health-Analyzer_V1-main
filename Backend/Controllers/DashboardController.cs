using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Security.Claims;

// DashboardController provides three consolidated endpoints:
//   GET /api/dashboard/summary  — spending overview
//   GET /api/dashboard/risk     — latest ML risk prediction (generates if none exists)
//   GET /api/dashboard/insights — latest generated insights

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly TransactionService     _transactionService;
    private readonly RiskPredictionService  _riskPredictionService;
    private readonly IRiskPredictionRepository _riskRepo;
    private readonly InsightsService        _insightsService;
    private readonly IInsightRepository     _insightRepo;
    private readonly ILogger<DashboardController> _logger;

    public DashboardController(
        TransactionService transactionService,
        RiskPredictionService riskPredictionService,
        IRiskPredictionRepository riskRepo,
        InsightsService insightsService,
        IInsightRepository insightRepo,
        ILogger<DashboardController> logger)
    {
        _transactionService    = transactionService;
        _riskPredictionService = riskPredictionService;
        _riskRepo              = riskRepo;
        _insightsService       = insightsService;
        _insightRepo           = insightRepo;
        _logger                = logger;
    }

    // ─── GET /api/dashboard/summary ───────────────────────────────────────

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary()
    {
        var userId = GetUserIdFromToken();
        _logger.LogInformation("Dashboard summary requested for UserId={UserId}", userId);

        var summary = await _transactionService.GetSummaryAsync(userId);

        // Map to DashboardSummaryDto — only expose what the UI needs
        var dto = new DashboardSummaryDto
        {
            TotalSpent                = summary.TotalSpent,
            TotalTransactions         = summary.TotalTransactions,
            AverageMonthlySpend       = summary.AverageMonthlySpend,
            HighestSpendingCategory   = summary.HighestSpendingCategory,
            CategoryBreakdown         = summary.CategoryBreakdown,
            MonthlyBreakdown          = summary.MonthlyBreakdown
        };

        return Ok(dto);
    }

    // ─── GET /api/dashboard/risk ──────────────────────────────────────────

    [HttpGet("risk")]
    public async Task<IActionResult> GetRisk()
    {
        var userId = GetUserIdFromToken();
        _logger.LogInformation("Risk prediction requested for UserId={UserId}", userId);

        // Get spending data needed for prediction
        var summary = await _transactionService.GetSummaryAsync(userId);

        if (summary.TotalTransactions == 0)
        {
            return Ok(new RiskDto
            {
                RiskLevel   = "Unknown",
                RiskScore   = 0,
                PredictedAt = DateTime.UtcNow,
                Description = "No transactions found. Upload your bank statement to get a risk assessment."
            });
        }

        // Extract inputs for the ML model
        var topCategoryPct = summary.CategoryBreakdown.Count > 0
            ? (float)summary.CategoryBreakdown[0].PercentageOfTotal
            : 0f;

        var monthCount = summary.MonthlyBreakdown.Count;

        // Run prediction
        var (riskLevel, riskScore) = _riskPredictionService.Predict(
            monthlyAvgSpend:       summary.AverageMonthlySpend,
            totalTransactions:     summary.TotalTransactions,
            monthCount:            monthCount,
            topCategoryPercentage: topCategoryPct,
            categoryCount:         summary.CategoryBreakdown.Count);

        // Persist the prediction
        var prediction = new RiskPrediction
        {
            UserId            = userId,
            RiskScore         = riskScore,
            RiskLevel         = riskLevel,
            MonthlyAvgSpend   = summary.AverageMonthlySpend,
            TotalTransactions = summary.TotalTransactions,
            CategoryCount     = summary.CategoryBreakdown.Count,
            PredictedAt       = DateTime.UtcNow
        };

        await _riskRepo.SaveAsync(prediction);

        var description = riskLevel switch
        {
            "Low"    => "Your spending patterns look healthy. Keep maintaining your financial discipline.",
            "Medium" => "Some spending patterns need attention. Review your top expense categories.",
            "High"   => "Your spending patterns indicate high financial risk. Immediate action recommended.",
            _        => "Risk level could not be determined."
        };

        return Ok(new RiskDto
        {
            RiskLevel   = riskLevel,
            RiskScore   = riskScore,
            PredictedAt = prediction.PredictedAt,
            Description = description
        });
    }

    // ─── GET /api/dashboard/insights ─────────────────────────────────────

    [HttpGet("insights")]
    public async Task<IActionResult> GetInsights()
    {
        var userId = GetUserIdFromToken();
        _logger.LogInformation("Insights requested for UserId={UserId}", userId);

        var summary    = await _transactionService.GetSummaryAsync(userId);
        var latestRisk = await _riskRepo.GetLatestByUserIdAsync(userId);
        var riskLevel  = latestRisk?.RiskLevel ?? "Low";

        if (summary.TotalTransactions == 0)
        {
            return Ok(new List<InsightDto>());
        }

        // Generate fresh insights based on current data
        var insights = await _insightsService.GenerateAndSaveAsync(
            userId, summary, riskLevel);

        var dtos = insights.Select(i => new InsightDto
        {
            Id          = i.Id,
            Title       = i.Title,
            Message     = i.Message,
            Priority    = i.Priority,
            Type        = i.Type,
            GeneratedAt = i.GeneratedAt
        }).ToList();

        return Ok(dtos);
    }

    // ─── HELPER ───────────────────────────────────────────────────────────

    private int GetUserIdFromToken()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)
                 ?? User.FindFirst("userId");

        if (claim == null || !int.TryParse(claim.Value, out var userId))
            throw new UnauthorizedAccessException("Invalid token.");

        return userId;
    }
}