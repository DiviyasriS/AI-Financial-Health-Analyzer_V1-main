public class TransactionService : ITransactionService
{
    private readonly ITransactionRepository      _transactionRepository;
    private readonly CsvService                  _csvService;
    private readonly XlsxService                 _xlsxService;
    private readonly PdfService                  _pdfService;         // ← NEW
    private readonly ILogger<TransactionService> _logger;

    public TransactionService(
        ITransactionRepository      transactionRepository,
        CsvService                  csvService,
        XlsxService                 xlsxService,
        PdfService                  pdfService,                        // ← NEW
        ILogger<TransactionService> logger)
    {
        _transactionRepository = transactionRepository;
        _csvService            = csvService;
        _xlsxService           = xlsxService;
        _pdfService            = pdfService;                           // ← NEW
        _logger                = logger;
    }

    // ─── Upload + Deduplication ───────────────────────────────────────────

    public async Task<FileProcessingResultDto> ProcessAndSaveAsync(
        Stream fileStream, string fileName, int userId)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant().Trim();

        _logger.LogInformation("Processing {Extension} file for user {UserId}", extension, userId);

        // ── Route to the correct parser ───────────────────────────────────
        // PDF is added here; CSV and XLSX paths are completely unchanged.
        ParsedFileResult parsed = extension switch
        {
            ".csv"  => await _csvService.ParseAsync(fileStream, userId),
            ".xlsx" => await _xlsxService.ParseAsync(fileStream, userId),
            ".xls"  => await _xlsxService.ParseAsync(fileStream, userId),
            ".pdf"  => await _pdfService.ParseAsync(fileStream, userId),   // ← NEW
            _       => throw new InvalidOperationException($"Unsupported file type: {extension}")
        };

        if (parsed.Transactions.Count == 0)
        {
            return new FileProcessingResultDto
            {
                SavedCount     = 0,
                DuplicateCount = parsed.DuplicateRows,
                SkippedCount   = parsed.SkippedRows,
                TotalRowsFound = parsed.TotalRowsFound,
                FileType       = extension.TrimStart('.').ToUpperInvariant(),
                Message        = BuildSummaryMessage(parsed.TotalRowsFound, 0, parsed.DuplicateRows, parsed.SkippedRows)
            };
        }

        // ── FIX: Batch duplicate check (eliminates N+1 queries) ───────────
        //
        // OLD approach: for each parsed transaction, fire one SQL query:
        //   DuplicateExistsAsync(userId, date, description, amount)
        // For a 500-row CSV this is 500 round-trips to the database.
        //
        // NEW approach:
        //   1. Determine the date range of the parsed rows
        //   2. Fetch ALL existing transactions for that user in that range (1 query)
        //   3. Build an in-memory HashSet of (date, description, amount) keys
        //   4. Filter the parsed rows locally — O(1) per lookup

        var minDate = parsed.Transactions.Min(t => t.Date.Date);
        var maxDate = parsed.Transactions.Max(t => t.Date.Date);

        var existingInRange = await _transactionRepository
            .GetByUserIdAndDateRangeAsync(userId, minDate, maxDate);

        // Build a set of composite keys for fast O(1) duplicate detection
        var existingKeys = existingInRange
            .Select(t => MakeDuplicateKey(t.Date, t.Description, t.Amount))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var nonDuplicates  = new List<Transaction>();
        var duplicateCount = parsed.DuplicateRows;

        foreach (var tx in parsed.Transactions)
        {
            var key = MakeDuplicateKey(tx.Date, tx.Description, tx.Amount);
            if (existingKeys.Contains(key))
                duplicateCount++;
            else
                nonDuplicates.Add(tx);
        }

        // ── Monthly duplicate upload detection ────────────────────────────
        string? monthWarning = null;

        if (nonDuplicates.Count > 0)
        {
            var months = nonDuplicates
                .Select(t => (t.Date.Year, t.Date.Month))
                .Distinct()
                .ToList();

            if (months.Count == 1)
            {
                var (year, month) = months.First();
                var existingCount = await _transactionRepository
                    .GetTransactionCountByMonthAsync(userId, year, month);

                if (existingCount > 0)
                {
                    var monthName = new DateTime(year, month, 1).ToString("MMMM yyyy");
                    monthWarning = $"Warning: {existingCount} transactions for {monthName} " +
                                   $"already exist. Duplicates were skipped automatically.";
                }
            }
        }

        await _transactionRepository.AddRangeAsync(nonDuplicates);

        _logger.LogInformation(
            "File processed for user {UserId}: {Saved} saved, {Duplicates} duplicates, {Skipped} skipped",
            userId, nonDuplicates.Count, duplicateCount, parsed.SkippedRows);

        return new FileProcessingResultDto
        {
            SavedCount     = nonDuplicates.Count,
            DuplicateCount = duplicateCount,
            SkippedCount   = parsed.SkippedRows,
            TotalRowsFound = parsed.TotalRowsFound,
            FileType       = extension.TrimStart('.').ToUpperInvariant(),
            Message        = BuildSummaryMessage(parsed.TotalRowsFound, nonDuplicates.Count, duplicateCount, parsed.SkippedRows),
            MonthWarning   = monthWarning
        };
    }

    // ─── Queries ──────────────────────────────────────────────────────────

    public async Task<List<TransactionDto>> GetTransactionsAsync(int userId)
    {
        _logger.LogDebug("Fetching transactions for user {UserId}", userId);
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

    public async Task<SpendingSummaryDto> GetSummaryAsync(int userId)
    {
        _logger.LogDebug("Computing spending summary for user {UserId}", userId);
        var transactions = await _transactionRepository.GetByUserIdAsync(userId);

        if (transactions.Count == 0)
        {
            return new SpendingSummaryDto
            {
                TotalSpent               = 0,
                TotalTransactions        = 0,
                AverageMonthlySpend      = 0,
                AverageTransactionAmount = 0,
                HighestSpendingCategory  = "N/A",
                CategoryBreakdown        = new List<CategorySummaryDto>(),
                MonthlyBreakdown         = new List<MonthlySummaryDto>()
            };
        }

        var totalSpent = transactions.Sum(t => t.Amount);

        var categoryBreakdown = transactions
            .GroupBy(t => t.Category)
            .Select(g =>
            {
                var categoryTotal = g.Sum(t => t.Amount);
                return new CategorySummaryDto
                {
                    Category          = g.Key,
                    Total             = categoryTotal,
                    TransactionCount  = g.Count(),
                    PercentageOfTotal = totalSpent > 0
                        ? Math.Round((categoryTotal / totalSpent) * 100, 2)
                        : 0,
                    TopTransactions   = g
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

            var change    = previous != null ? current.Total - previous.Total : (decimal?)null;
            var changePct = previous?.Total > 0
                ? Math.Round(((current.Total - previous.Total) / previous.Total) * 100, 2)
                : (decimal?)null;

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

        // Most-recent-first for display
        monthlyBreakdown.Reverse();

        var biggestTransaction = transactions.OrderByDescending(t => t.Amount).First();

        return new SpendingSummaryDto
        {
            TotalSpent               = totalSpent,
            TotalTransactions        = transactions.Count,
            AverageTransactionAmount = Math.Round(totalSpent / transactions.Count, 2),
            AverageMonthlySpend      = monthlyBreakdown.Count > 0
                ? Math.Round(monthlyBreakdown.Average(m => m.Total), 2)
                : 0,
            HighestSpendingCategory = categoryBreakdown.First().Category,
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

    // ─── Helpers ──────────────────────────────────────────────────────────

    // Creates a composite key used for in-memory duplicate detection.
    // Format: "yyyy-MM-dd|description|amount"
    private static string MakeDuplicateKey(DateTime date, string description, decimal amount)
        => $"{date.Date:yyyy-MM-dd}|{description}|{amount}";

    private static string BuildSummaryMessage(int total, int saved, int duplicates, int skipped)
    {
        if (total == 0)
            return "File was empty or had no valid data rows.";
        if (saved == 0 && duplicates > 0)
            return "All transactions in this file already exist. No new records added.";

        var msg = $"Processed {total} rows. Saved: {saved}";
        if (duplicates > 0) msg += $", Duplicates skipped: {duplicates}";
        if (skipped > 0)    msg += $", Invalid rows skipped: {skipped}";
        return msg + ".";
    }
}