using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TransactionController : ControllerBase
{
    private readonly ITransactionService _transactionService;
    private readonly ILogger<TransactionController> _logger;

    private static readonly string[] AllowedExtensions = { ".csv", ".xlsx", ".xls", ".pdf" };

    public TransactionController(
        ITransactionService transactionService,
        ILogger<TransactionController> logger)
    {
        _transactionService = transactionService;
        _logger = logger;
    }

    [HttpPost("upload")]
    public async Task<IActionResult> UploadFile(IFormFile file)
    {
        if (!TryGetUserIdFromToken(out int userId))
        {
            _logger.LogWarning("File upload rejected because token was invalid.");
            return Unauthorized(new { message = "Invalid token." });
        }

        if (file == null || file.Length == 0)
        {
            _logger.LogWarning("File upload rejected for UserId={UserId}: empty file.", userId);
            return BadRequest(new { message = "Please upload a valid file." });
        }

        if (file.Length > 10 * 1024 * 1024)
        {
            _logger.LogWarning(
                "File upload rejected for UserId={UserId}: file too large. Size={FileSizeBytes}",
                userId,
                file.Length);

            return BadRequest(new { message = "File size must be under 10MB." });
        }

        string extension = Path.GetExtension(file.FileName).ToLowerInvariant().Trim();

        if (!AllowedExtensions.Contains(extension))
        {
            _logger.LogWarning(
                "File upload rejected for UserId={UserId}: unsupported extension {Extension}",
                userId,
                extension);

            return BadRequest(new
            {
                message = $"Unsupported file type '{extension}'. Supported: CSV, XLSX, XLS, PDF."
            });
        }

        _logger.LogInformation(
            "File upload started for UserId={UserId}. FileName={FileName}, Extension={Extension}, Size={FileSizeBytes}",
            userId,
            file.FileName,
            extension,
            file.Length);

        using Stream stream = file.OpenReadStream();

        FileProcessingResultDto result = await _transactionService.ProcessAndSaveAsync(
            stream,
            file.FileName,
            userId);

        _logger.LogInformation(
            "File upload completed for UserId={UserId}. FileName={FileName}, Imported={DuplicateCount}, Skipped={SkippedCount}",
            userId,
            file.FileName,
            result.DuplicateCount,
            result.SkippedCount);

        return Ok(result);
    }

    [HttpGet]
    public async Task<IActionResult> GetTransactions()
    {
        if (!TryGetUserIdFromToken(out int userId))
        {
            _logger.LogWarning("Transaction fetch rejected because token was invalid.");
            return Unauthorized(new { message = "Invalid token." });
        }

        List<TransactionDto> transactions = await _transactionService.GetTransactionsAsync(userId);

        _logger.LogInformation(
            "Transactions fetched for UserId={UserId}. Count={TransactionCount}",
            userId,
            transactions.Count);

        return Ok(transactions);
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary()
    {
        if (!TryGetUserIdFromToken(out int userId))
        {
            _logger.LogWarning("Transaction summary request rejected because token was invalid.");
            return Unauthorized(new { message = "Invalid token." });
        }

        SpendingSummaryDto summary = await _transactionService.GetSummaryAsync(userId);

        _logger.LogInformation("Spending summary generated for UserId={UserId}", userId);

        return Ok(summary);
    }

    private bool TryGetUserIdFromToken(out int userId)
    {
        userId = 0;

        Claim? claim = User.FindFirst(ClaimTypes.NameIdentifier)
                    ?? User.FindFirst("userId");

        return claim != null && int.TryParse(claim.Value, out userId);
    }
}