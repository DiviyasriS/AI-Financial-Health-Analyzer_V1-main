public class TransactionService : ITransactionService
{
    private readonly ITransactionRepository _transactionRepository;
    private readonly CsvService _csvService;
    private readonly XlsxService _xlsxService;
    private readonly ILogger<TransactionService> _logger;

    public TransactionService(
        ITransactionRepository transactionRepository,
        CsvService csvService,
        XlsxService xlsxService,
        ILogger<TransactionService> logger)
    {
        _transactionRepository = transactionRepository;
        _csvService = csvService;
        _xlsxService = xlsxService;
        _logger = logger;
    }

    public async Task<FileProcessingResultDto> ProcessAndSaveAsync(
        Stream fileStream, string fileName, int userId)
    {
        string extension = Path.GetExtension(fileName).ToLowerInvariant().Trim();

        _logger.LogInformation("Processing {Extension} file for user {UserId}", extension, userId);

        ParsedFileResult parsed = extension switch
        {
            ".csv"  => await _csvService.ParseAsync(fileStream, userId),
            ".xlsx" => await _xlsxService.ParseAsync(fileStream, userId),
            ".xls"  => await _xlsxService.ParseAsync(fileStream, userId),
            _       => throw new InvalidOperationException($"Unsupported file type: {extension}")
        };

        // ── Deduplicate against existing records ──────────────────────────
        // Fetch existing transactions for the relevant date range to avoid N+1
        List<Transaction> nonDuplicates = new();
        int duplicateRows = parsed.DuplicateRows; // preserve any parser-level dupes (none currently)

        foreach (Transaction tx in parsed.Transactions)
        {
            bool isDuplicate = await _transactionRepository.DuplicateExistsAsync(
                userId, tx.Date, tx.Description, tx.Amount);

            if (isDuplicate)
                duplicateRows++;
            else
                nonDuplicates.Add(tx);
        }

        // ── Monthly duplicate upload detection ────────────────────────────
        string? monthWarning = null;

        if (nonDuplicates.Count > 0)
        {
            List<(int Year, int Month)> months = nonDuplicates
                .Select(t => (t.Date.Year, t.Date.Month))
                .Distinct()
                .ToList();

            if (months.Count == 1)
            {
                (int year, int month) = months.First();
                int existingCount = await _transactionRepository
                    .GetTransactionCountByMonthAsync(userId, year, month);

                if (existingCount > 0)
                {
                    string monthName = new DateTime(year, month, 1).ToString("MMMM yyyy");
                    monthWarning = $"Warning: {existingCount} transactions for {monthName} " +
                                   $"already exist. Duplicates were skipped automatically.";
                }
            }
        }

        await _transactionRepository.AddRangeAsync(nonDuplicates);

        _logger.LogInformation(
            "File processed for user {UserId}: {Saved} saved, {Duplicates} duplicates, {Skipped} skipped",
            userId, nonDuplicates.Count, duplicateRows, parsed.SkippedRows);

        return new FileProcessingResultDto
        {
            SavedCount     = nonDuplicates.Count,
            DuplicateCount = duplicateRows,
            SkippedCount   = parsed.SkippedRows,
            TotalRowsFound = parsed.TotalRowsFound,
            FileType       = extension.TrimStart('.').ToUpperInvariant(),
            Message        = BuildSummaryMessage(parsed.TotalRowsFound, nonDuplicates.Count, duplicateRows, parsed.SkippedRows),
            MonthWarning   = monthWarning
        };
    }

    public async Task<List<TransactionDto>> GetTransactionsAsync(int userId)
    {
        _logger.LogDebug("Fetching transactions for user {UserId}", userId);
        List<Transaction> transactions = await _transactionRepository.GetByUserIdAsync(userId);

        return transactions.Select(t => new TransactionDto
        {
            Id          = t.Id,
            Date        = t.Date,
            Description = t.Description,
            Amount      = t.Amount,
            Category    = t.Category
        }).ToList();
    }

    public async Task<SpendingSummaryDto> GetSummaryAsync(int userId)
    {
        _logger.LogDebug("Computing spending summary for user {UserId}", userId);
        List<Transaction> transactions = await _transactionRepository.GetByUserIdAsync(userId);

        if (transactions.Count == 0)
        {
            return new SpendingSummaryDto
            {
                TotalSpent              = 0,
                TotalTransactions       = 0,
                AverageMonthlySpend     = 0,
                AverageTransactionAmount = 0,
                HighestSpendingCategory = "N/A",
                CategoryBreakdown       = new List<CategorySummaryDto>(),
                MonthlyBreakdown        = new List<MonthlySummaryDto>()
            };
        }

        decimal totalSpent = transactions.Sum(t => t.Amount);

        List<CategorySummaryDto> categoryBreakdown = transactions
            .GroupBy(t => t.Category)
            .Select(g =>
            {
                decimal categoryTotal = g.Sum(t => t.Amount);
                return new CategorySummaryDto
                {
                    Category         = g.Key,
                    Total            = categoryTotal,
                    TransactionCount = g.Count(),
                    PercentageOfTotal = totalSpent > 0
                        ? Math.Round((categoryTotal / totalSpent) * 100, 2)
                        : 0,
                    TopTransactions = g
                        .OrderByDescending(t => t.Amount)
                        .Take(3)
                        .Select(t => new TransactionDto
                        {
                            Id          = t.Id,
                            Date        = t.Date,
                            Description = t.Description,
                            Amount      = t.Amount,
                            Category    = t.Category
                        })
                        .ToList()
                };
            })
            .OrderByDescending(c => c.Total)
            .ToList();

        var monthlyRaw = transactions
            .GroupBy(t => new { t.Date.Year, t.Date.Month })
            .Select(g => new
            {
                g.Key.Year,
                g.Key.Month,
                Total            = g.Sum(t => t.Amount),
                TransactionCount = g.Count()
            })
            .OrderBy(m => m.Year).ThenBy(m => m.Month)
            .ToList();

        var monthlyBreakdown = new List<MonthlySummaryDto>();

        for (int i = 0; i < monthlyRaw.Count; i++)
        {
            var current  = monthlyRaw[i];
            var previous = i > 0 ? monthlyRaw[i - 1] : null;

            decimal? change    = previous != null ? current.Total - previous.Total : null;
            decimal? changePct = previous?.Total > 0
                ? Math.Round(((current.Total - previous.Total) / previous.Total) * 100, 2)
                : null;

            monthlyBreakdown.Add(new MonthlySummaryDto
            {
                Year             = current.Year,
                Month            = current.Month,
                MonthName        = new DateTime(current.Year, current.Month, 1).ToString("MMMM yyyy"),
                Total            = current.Total,
                TransactionCount = current.TransactionCount,
                ChangeFromPreviousMonth           = change,
                PercentageChangeFromPreviousMonth = changePct
            });
        }

        monthlyBreakdown.Reverse();

        Transaction biggestTransaction = transactions.OrderByDescending(t => t.Amount).First();

        return new SpendingSummaryDto
        {
            TotalSpent               = totalSpent,
            TotalTransactions        = transactions.Count,
            AverageTransactionAmount = Math.Round(totalSpent / transactions.Count, 2),
            AverageMonthlySpend      = monthlyBreakdown.Count > 0
                ? Math.Round(monthlyBreakdown.Average(m => m.Total), 2)
                : 0,
            HighestSpendingCategory  = categoryBreakdown.First().Category,
            BiggestTransaction = new TransactionDto
            {
                Id          = biggestTransaction.Id,
                Date        = biggestTransaction.Date,
                Description = biggestTransaction.Description,
                Amount      = biggestTransaction.Amount,
                Category    = biggestTransaction.Category
            },
            CategoryBreakdown = categoryBreakdown,
            MonthlyBreakdown  = monthlyBreakdown
        };
    }

    private static string BuildSummaryMessage(int total, int saved, int duplicates, int skipped)
    {
        if (total == 0)
            return "File was empty or had no valid data rows.";
        if (saved == 0 && duplicates > 0)
            return "All transactions in this file already exist. No new records added.";

        string msg = $"Processed {total} rows. Saved: {saved}";
        if (duplicates > 0) msg += $", Duplicates skipped: {duplicates}";
        if (skipped > 0)    msg += $", Invalid rows skipped: {skipped}";
        return msg + ".";
    }
}