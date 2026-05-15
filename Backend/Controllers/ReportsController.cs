using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ReportsController : ControllerBase
{
    private readonly IReportService _reportService;
    private readonly ILogger<ReportsController> _logger;

    public ReportsController(
        IReportService reportService,
        ILogger<ReportsController> logger)
    {
        _reportService = reportService;
        _logger = logger;
    }

    [HttpGet("financial/pdf")]
    public async Task<IActionResult> DownloadFinancialReport()
    {
        if (!TryGetUserIdFromToken(out int userId))
            return Unauthorized(ApiResponse<object>.Fail("Invalid token."));

        try
        {
            byte[] pdfBytes = await _reportService.GenerateFinancialReportPdfAsync(userId);
            string fileName = $"financial-health-report-{DateTime.UtcNow:yyyyMMddHHmmss}.pdf";

            return File(pdfBytes, "application/pdf", fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PDF report download failed for UserId={UserId}", userId);
            return StatusCode(500, ApiResponse<object>.Fail("Failed to generate PDF report."));
        }
    }

    private bool TryGetUserIdFromToken(out int userId)
    {
        userId = 0;
        Claim? claim = User.FindFirst(ClaimTypes.NameIdentifier)
                    ?? User.FindFirst("userId");

        return claim is not null && int.TryParse(claim.Value, out userId);
    }
}