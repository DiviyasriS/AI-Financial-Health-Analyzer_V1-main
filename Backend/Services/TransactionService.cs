using System.Globalization;

// TransactionService contains all business logic for transactions
// It sits between the Controller and the Repository
// Controller → TransactionService → ITransactionRepository → Database

public class TransactionService
{
    private readonly ITransactionRepository _transactionRepository;
    private readonly CsvService _csvService;

    public TransactionService(
        ITransactionRepository transactionRepository,
        CsvService csvService)
    {
        _transactionRepository = transactionRepository;
        _csvService = csvService;
    }

    // ─── UPLOAD AND PROCESS FILE ─────────────────────────────────────────────

    public async Task<UploadResultDto> ProcessAndSaveAsync(Stream fileStream, int userId)
    {
        // Step 1: Parse the CSV into transaction objects (duplicates already filtered)
        var transactions = await _csvService.ParseCsvAsync(
            fileStream, userId, _transactionRepository);

        // Step 2: Save all parsed transactions in one DB call
        await _transactionRepository.AddRangeAsync(transactions);

        return new UploadResultDto
        {
            Message = "File processed successfully.",
            SavedCount = transactions.Count
        };
    }

    // ─── GET TRANSACTIONS ─────────────────────────────────────────────────────

    public async Task<List<TransactionDto>> GetTransactionsAsync(int userId)
    {
        var transactions = await _transactionRepository.GetByUserIdAsync(userId);

        // Map Entity → DTO before returning to the controller
        // Never return raw DB entities directly to the API response
        return transactions.Select(t => new TransactionDto
        {
            Id          = t.Id,
            Date        = t.Date,
            Description = t.Description,
            Amount      = t.Amount,
            Category    = t.Category
        }).ToList();
    }

    // ─── SPENDING SUMMARY ─────────────────────────────────────────────────────

    public async Task<SpendingSummaryDto> GetSummaryAsync(int userId)
    {
        var transactions = await _transactionRepository.GetByUserIdAsync(userId);

        if (transactions.Count == 0)
        {
            return new SpendingSummaryDto
            {
                TotalSpent = 0,
                CategoryBreakdown = new List<CategorySummaryDto>(),
                MonthlyBreakdown = new List<MonthlySummaryDto>()
            };
        }

        // ── Category breakdown ────────────────────────────────────────────────
        var categoryBreakdown = transactions
            .GroupBy(t => t.Category)
            .Select(g => new CategorySummaryDto
            {
                Category         = g.Key,
                Total            = g.Sum(t => t.Amount),
                TransactionCount = g.Count()
            })
            .OrderByDescending(c => c.Total)
            .ToList();

        // ── Monthly breakdown ─────────────────────────────────────────────────
        var monthlyBreakdown = transactions
            .GroupBy(t => new { t.Date.Year, t.Date.Month })
            .Select(g => new MonthlySummaryDto
            {
                Year             = g.Key.Year,
                Month            = g.Key.Month,
                MonthName        = new DateTime(g.Key.Year, g.Key.Month, 1)
                                       .ToString("MMMM yyyy"),
                Total            = g.Sum(t => t.Amount),
                TransactionCount = g.Count()
            })
            .OrderByDescending(m => m.Year)
            .ThenByDescending(m => m.Month)
            .ToList();

        return new SpendingSummaryDto
        {
            TotalSpent        = transactions.Sum(t => t.Amount),
            CategoryBreakdown = categoryBreakdown,
            MonthlyBreakdown  = monthlyBreakdown
        };
    }
}

// ─── HELPER DTO (only used internally for upload result) ──────────────────────

public class UploadResultDto
{
    public string Message { get; set; } = string.Empty;
    public int SavedCount { get; set; }
}