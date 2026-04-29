using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/[controller]")]
public class TransactionController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly CsvService _csvService;

    public TransactionController(AppDbContext context, CsvService csvService)
    {
        _context = context;
        _csvService = csvService;
    }

    [HttpPost("upload")]
    public async Task<IActionResult> UploadCsv(IFormFile file, int userId)
    {
        if (file == null || file.Length == 0)
            return BadRequest("Invalid file");

        using var stream = file.OpenReadStream();

        var transactions = _csvService.ProcessCsv(stream, userId, _context);

        await _context.Transactions.AddRangeAsync(transactions);
        await _context.SaveChangesAsync();

        return Ok(new { message = "File processed successfully", count = transactions.Count });
    }

    [HttpGet("{userId}")]
    public IActionResult GetTransactions(int userId)
    {
        var data = _context.Transactions.Where(t => t.UserId == userId).ToList();
        return Ok(data);
    }
    [HttpGet("summary/{userId}")]
public IActionResult GetSummary(int userId)
{
    var data = _context.Transactions
        .Where(t => t.UserId == userId)
        .ToList();

    var totalSpent = data.Sum(t => t.Amount);

    var categoryWise = data
        .GroupBy(t => t.Category)
        .Select(g => new
        {
            Category = g.Key,
            Total = g.Sum(x => x.Amount)
        });

    return Ok(new
    {
        TotalSpent = totalSpent,
        CategoryBreakdown = categoryWise
    });
}
}