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
    private readonly ITransactionService _transactionService;
    private readonly RiskPredictionService  _riskPredictionService;
    private readonly IRiskPredictionRepository _riskRepo;
    private readonly InsightsService        _insightsService;
    private readonly IInsightRepository     _insightRepo;
    private readonly ILogger<DashboardController> _logger;

    public DashboardController(
    ITransactionService transactionService,
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
    if (!TryGetUserIdFromToken(out int userId))
        return Unauthorized(ApiResponse<object>.Fail("Invalid token."));

    _logger.LogInformation("Dashboard summary requested for UserId={UserId}", userId);
    SpendingSummaryDto summary = await _transactionService.GetSummaryAsync(userId);
    var dto = new DashboardSummaryDto
    {
        TotalSpent              = summary.TotalSpent,
        TotalTransactions       = summary.TotalTransactions,
        AverageMonthlySpend     = summary.AverageMonthlySpend,
        HighestSpendingCategory = summary.HighestSpendingCategory,
        CategoryBreakdown       = summary.CategoryBreakdown,
        MonthlyBreakdown        = summary.MonthlyBreakdown
    };
    return Ok(dto);
}

[HttpGet("risk")]
public async Task<IActionResult> GetRisk()
{
    if (!TryGetUserIdFromToken(out int userId))
        return Unauthorized(ApiResponse<object>.Fail("Invalid token."));

    _logger.LogInformation("Risk prediction requested for UserId={UserId}", userId);
    SpendingSummaryDto summary = await _transactionService.GetSummaryAsync(userId);

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

    float topCategoryPct = summary.CategoryBreakdown.Count > 0
        ? (float)summary.CategoryBreakdown[0].PercentageOfTotal
        : 0f;

    (string riskLevel, float riskScore) = _riskPredictionService.Predict(
        monthlyAvgSpend:       summary.AverageMonthlySpend,
        totalTransactions:     summary.TotalTransactions,
        monthCount:            summary.MonthlyBreakdown.Count,
        topCategoryPercentage: topCategoryPct,
        categoryCount:         summary.CategoryBreakdown.Count);

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

    string description = riskLevel switch
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

[HttpGet("insights")]
public async Task<IActionResult> GetInsights()
{
    if (!TryGetUserIdFromToken(out int userId))
        return Unauthorized(ApiResponse<object>.Fail("Invalid token."));

    _logger.LogInformation("Insights requested for UserId={UserId}", userId);
    SpendingSummaryDto summary    = await _transactionService.GetSummaryAsync(userId);
    RiskPrediction? latestRisk    = await _riskRepo.GetLatestByUserIdAsync(userId);
    string riskLevel              = latestRisk?.RiskLevel ?? "Low";

    if (summary.TotalTransactions == 0)
        return Ok(new List<InsightDto>());

    List<Insight> insights = await _insightsService.GenerateAndSaveAsync(
        userId, summary, riskLevel);

    List<InsightDto> dtos = insights.Select(i => new InsightDto
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

private bool TryGetUserIdFromToken(out int userId)
{
    userId = 0;
    Claim? claim = User.FindFirst(ClaimTypes.NameIdentifier)
                ?? User.FindFirst("userId");
    return claim != null && int.TryParse(claim.Value, out userId);
}
}