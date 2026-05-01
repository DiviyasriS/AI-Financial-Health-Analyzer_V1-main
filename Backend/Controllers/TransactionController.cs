using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

// TransactionController is intentionally thin
// It receives the request, calls the service, returns the response
// Zero business logic lives here — it all lives in TransactionService

[ApiController]
[Route("api/[controller]")]
[Authorize] // every endpoint here requires a valid JWT token
public class TransactionController : ControllerBase
{
    private readonly TransactionService _transactionService;
    private static readonly string[] AllowedExtensions = { ".csv", ".xlsx", ".xls" };

    public TransactionController(TransactionService transactionService)
    {
        _transactionService = transactionService;
    }

    // ─── POST /api/transaction/upload?userId=1 ────────────────────────────────
[HttpPost("upload")]
public async Task<IActionResult> UploadFile(IFormFile file, int userId)
{
    if (file == null || file.Length == 0)
        return BadRequest(new { message = "Please upload a valid file." });

    if (file.Length > 10 * 1024 * 1024)
        return BadRequest(new { message = "File size must be under 10MB." });

    var extension = Path.GetExtension(file.FileName).ToLower().Trim();
    if (!AllowedExtensions.Contains(extension))
        return BadRequest(new
        {
            message = $"Unsupported file type '{extension}'. " +
                       "Please upload a .csv or .xlsx file."
        });

    using var stream = file.OpenReadStream();

    var result = await _transactionService.ProcessAndSaveAsync(
        stream, file.FileName, userId);

    return Ok(result);
}

    // ─── GET /api/transaction/{userId} ───────────────────────────────────────

    [HttpGet("{userId}")]
    public async Task<IActionResult> GetTransactions(int userId)
    {
        var transactions = await _transactionService.GetTransactionsAsync(userId);

        if (transactions.Count == 0)
            return Ok(new { message = "No transactions found for this user.", data = transactions });

        return Ok(transactions);
    }

    // ─── GET /api/transaction/summary/{userId} ────────────────────────────────

    [HttpGet("summary/{userId}")]
    public async Task<IActionResult> GetSummary(int userId)
    {
        var summary = await _transactionService.GetSummaryAsync(userId);
        return Ok(summary);
    }
}