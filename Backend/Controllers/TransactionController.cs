using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TransactionController : ControllerBase
{
private readonly ITransactionService _transactionService;

    private static readonly string[] AllowedExtensions = { ".csv", ".xlsx", ".xls" };

public TransactionController(ITransactionService transactionService)
{
    _transactionService = transactionService;
}

    // ─── HELPER: Extract userId from the JWT token ────────────────────────────
    // This is safer than trusting userId from the URL/query param
    // The token is signed — it cannot be tampered with

private bool TryGetUserIdFromToken(out int userId)
{
    userId = 0;
    System.Security.Claims.Claim? claim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)
                                        ?? User.FindFirst("userId");
    return claim != null && int.TryParse(claim.Value, out userId);
}

    // ─── POST /api/transaction/upload ─────────────────────────────────────────
    // userId is now read from the token — not from query param

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
        return BadRequest(new { message = $"Unsupported file type '{extension}'." });

    using Stream stream = file.OpenReadStream();
    FileProcessingResultDto result = await _transactionService.ProcessAndSaveAsync(
        stream, file.FileName, userId);

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
}