// TransactionService orchestrates file processing
// It decides which parser to use based on file extension
// Controller → TransactionService → CsvService or XlsxService → ITransactionRepository

public class TransactionService
{
    private readonly ITransactionRepository _transactionRepository;
    private readonly CsvService _csvService;
    private readonly XlsxService _xlsxService;

    public TransactionService(
        ITransactionRepository transactionRepository,
        CsvService csvService,
        XlsxService xlsxService)
    {
        _transactionRepository = transactionRepository;
        _csvService = csvService;
        _xlsxService = xlsxService;
    }

    // ─── UPLOAD AND PROCESS FILE ─────────────────────────────────────────────

    public async Task<FileProcessingResultDto> ProcessAndSaveAsync(
        Stream fileStream, string fileName, int userId)
    {
        var extension = Path.GetExtension(fileName).ToLower().Trim();

        // Route to the correct parser based on file type
        ParsedFileResult parsed;

        if (extension == ".csv")
        {
            parsed = await _csvService.ParseAsync(fileStream, userId, _transactionRepository);
        }
        else if (extension == ".xlsx" || extension == ".xls")
        {
            parsed = await _xlsxService.ParseAsync(fileStream, userId, _transactionRepository);
        }
        else
        {
            // This should not happen — controller validates extension first
            // But we handle it defensively here too
            throw new InvalidOperationException(
                $"Unsupported file type: {extension}. Only .csv and .xlsx are supported.");
        }

        // Save all valid transactions in a single DB call
        await _transactionRepository.AddRangeAsync(parsed.Transactions);

        // Build a detailed response for the frontend
        return new FileProcessingResultDto
        {
            SavedCount      = parsed.Transactions.Count,
            DuplicateCount  = parsed.DuplicateRows,
            SkippedCount    = parsed.SkippedRows,
            TotalRowsFound  = parsed.TotalRowsFound,
            FileType        = extension.TrimStart('.').ToUpper(),
            Message         = BuildSummaryMessage(parsed, extension)
        };
    }

    // ─── GET TRANSACTIONS ─────────────────────────────────────────────────────

    public async Task<List<TransactionDto>> GetTransactionsAsync(int userId)
    {
        var transactions = await _transactionRepository.GetByUserIdAsync(userId);

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
                TotalSpent        = 0,
                CategoryBreakdown = new List<CategorySummaryDto>(),
                MonthlyBreakdown  = new List<MonthlySummaryDto>()
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

    // ─── PRIVATE HELPER ───────────────────────────────────────────────────────

    private string BuildSummaryMessage(ParsedFileResult parsed, string extension)
    {
        if (parsed.TotalRowsFound == 0)
            return "File was empty or had no valid data rows.";

        if (parsed.Transactions.Count == 0 && parsed.DuplicateRows > 0)
            return "All transactions in this file already exist. No new records added.";

        var msg = $"Processed {parsed.TotalRowsFound} rows. " +
                  $"Saved: {parsed.Transactions.Count}";

        if (parsed.DuplicateRows > 0)
            msg += $", Duplicates skipped: {parsed.DuplicateRows}";

        if (parsed.SkippedRows > 0)
            msg += $", Invalid rows skipped: {parsed.SkippedRows}";

        return msg + ".";
    }
}