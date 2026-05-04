using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TransactionController : ControllerBase
{
    private readonly TransactionService _transactionService;

    private static readonly string[] AllowedExtensions = { ".csv", ".xlsx", ".xls" };

    public TransactionController(TransactionService transactionService)
    {
        _transactionService = transactionService;
    }

    // ─── HELPER: Extract userId from the JWT token ────────────────────────────
    // This is safer than trusting userId from the URL/query param
    // The token is signed — it cannot be tampered with

private bool TryGetUserId(out int userId)
{
    userId = 0; // ensure it's always assigned

    var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)
                   ?? User.FindFirst("userId");

    return userIdClaim != null && int.TryParse(userIdClaim.Value, out userId);
}

    // ─── POST /api/transaction/upload ─────────────────────────────────────────
    // userId is now read from the token — not from query param

    [HttpPost("upload")]
    public async Task<IActionResult> UploadFile(IFormFile file)
    {
        



        if (file == null || file.Length == 0)
            return BadRequest(new { message = "Please upload a valid file." });

        if (file.Length > 10 * 1024 * 1024)
            return BadRequest(new { message = "File size must be under 10MB." });

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension))
            return BadRequest(new
            {
                message = $"Unsupported file type '{extension}'. " +
                           "Please upload a .csv or .xlsx file."
            });

        // Get userId from token — not from request
        if (!TryGetUserId(out var userId))
        return Unauthorized(new { message = "Invalid token: userId not found." });

        using var stream = file.OpenReadStream();

        var result = await _transactionService.ProcessAndSaveAsync(
            stream, file.FileName, userId);

        return Ok(result);
    }

    // ─── GET /api/transaction ─────────────────────────────────────────────────
    // No userId in URL — read from token
[HttpGet]
public async Task<IActionResult> GetTransactions()
{
    if (!TryGetUserId(out var userId))
    {
        return Unauthorized(new { message = "Invalid token: userId not found." });
    }


    var transactions = await _transactionService.GetTransactionsAsync(userId);
    return Ok(transactions);
}

    // ─── GET /api/transaction/summary ─────────────────────────────────────────
    // No userId in URL — read from token

[HttpGet("summary")]
public async Task<IActionResult> GetSummary()
{
    if (!TryGetUserId(out var userId))
        return Unauthorized(new { message = "Invalid token: userId not found." });

    var summary = await _transactionService.GetSummaryAsync(userId);
    return Ok(summary);
}
}