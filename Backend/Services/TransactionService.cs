public class TransactionService : ITransactionService
{
    private readonly ITransactionRepository _transactionRepository;
    private readonly CsvService _csvService;
    private readonly XlsxService _xlsxService;
    private readonly PdfService _pdfService;
    private readonly ILogger<TransactionService> _logger;

    public TransactionService(
        ITransactionRepository transactionRepository,
        CsvService csvService,
        XlsxService xlsxService,
        PdfService pdfService,
        ILogger<TransactionService> logger)
    {
        _transactionRepository = transactionRepository;
        _csvService = csvService;
        _xlsxService = xlsxService;
        _pdfService = pdfService;
        _logger = logger;
    }

    public async Task<FileProcessingResultDto> ProcessAndSaveAsync(
        Stream fileStream, string fileName, int userId)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant().Trim();

        _logger.LogInformation("Processing {Extension} file for user {UserId}", extension, userId);

        ParsedFileResult parsed = extension switch
        {
            ".csv"  => await _csvService.ParseAsync(fileStream, userId),
            ".xlsx" => await _xlsxService.ParseAsync(fileStream, userId),
            ".xls"  => await _xlsxService.ParseAsync(fileStream, userId),
            ".pdf"  => await _pdfService.ParseAsync(fileStream, userId),
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

        var minDate = parsed.Transactions.Min(t => t.Date.Date);
        var maxDate = parsed.Transactions.Max(t => t.Date.Date);

        var existingInRange = await _transactionRepository
            .GetByUserIdAndDateRangeAsync(userId, minDate, maxDate);

        var existingKeys = existingInRange
            .Select(t => MakeDuplicateKey(t.Date, t.Description, t.Amount))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var nonDuplicates = new List<Transaction>();
        var duplicateCount = parsed.DuplicateRows;

        foreach (var tx in parsed.Transactions)
        {
            var key = MakeDuplicateKey(tx.Date, tx.Description, tx.Amount);

            if (existingKeys.Contains(key))
                duplicateCount++;
            else
                nonDuplicates.Add(tx);
        }

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
                    monthWarning =
                        $"Warning: {existingCount} transactions for {monthName} already exist. " +
                        "Duplicates were skipped automatically.";
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
            Message        = BuildSummaryMessage(
                parsed.TotalRowsFound, nonDuplicates.Count, duplicateCount, parsed.SkippedRows),
            MonthWarning   = monthWarning
        };
    }

    public async Task<List<TransactionDto>> GetTransactionsAsync(int userId)
    {
        _logger.LogDebug("Fetching transactions for user {UserId}", userId);

        var transactions = await _transactionRepository.GetByUserIdAsync(userId);

        return transactions.Select(t => new TransactionDto
        {
            Id          = t.Id,
            Date        = t.Date,
            Description = t.Description,
            Amount      = Math.Abs(t.Amount),
            Category    = t.Category,
            IsCredit    = t.IsCredit,
            Type        = t.IsCredit ? "Credit" : "Debit"
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
                TotalSpent             = 0,
                TotalReceived          = 0,
                TotalTransactionVolume = 0,
                TotalTransactions      = 0,
                AverageMonthlySpend    = 0,
                AverageExpenseAmount   = 0,
                HighestSpendingCategory = "N/A",
                CategoryBreakdown      = new List<CategorySummaryDto>(),
                MonthlyBreakdown       = new List<MonthlySummaryDto>()
            };
        }

        // ── Totals ────────────────────────────────────────────────────────────
        var expenseTransactions  = transactions.Where(TransactionFilters.IsDebit).ToList();
        var receivedTransactions = transactions.Where(TransactionFilters.IsCredit).ToList();

        var totalSpent             = expenseTransactions.Sum(t => Math.Abs(t.Amount));
        var totalReceived          = receivedTransactions.Sum(t => Math.Abs(t.Amount));
        var totalTransactionVolume = totalSpent + totalReceived;

        // ── Analytics transactions (exclude credits and transfers) ────────────
        var analyticsTransactions = transactions
            .Where(TransactionFilters.IsSpendingAnalytics)
            .ToList();

        var analyticsTotalSpent = analyticsTransactions.Sum(t => Math.Abs(t.Amount));

        // ── Category breakdown ────────────────────────────────────────────────
        var categoryBreakdown = analyticsTransactions
            .GroupBy(t => string.IsNullOrWhiteSpace(t.Category) ? "Uncategorized" : t.Category)
            .Select(g =>
            {
                var categoryTotal = g.Sum(t => Math.Abs(t.Amount));

                return new CategorySummaryDto
                {
                    Category           = g.Key,
                    Total              = categoryTotal,
                    TransactionCount   = g.Count(),
                    PercentageOfTotal  = analyticsTotalSpent > 0
                        ? Math.Round((categoryTotal / analyticsTotalSpent) * 100, 2)
                        : 0,
                    TopTransactions    = g
                        .OrderByDescending(t => Math.Abs(t.Amount))
                        .Take(3)
                        .Select(t => new TransactionDto
                        {
                            Id          = t.Id,
                            Date        = t.Date,
                            Description = t.Description,
                            Amount      = Math.Abs(t.Amount),
                            Category    = t.Category,
                            IsCredit    = false,
                            Type        = "Debit"
                        })
                        .ToList()
                };
            })
            .OrderByDescending(c => c.Total)
            .ToList();

        // ── Monthly breakdown ──────────────────────────────────────────────────
        var monthlyRaw = expenseTransactions
            .GroupBy(t => new { t.Date.Year, t.Date.Month })
            .Select(g => new
            {
                g.Key.Year,
                g.Key.Month,
                TotalSpent       = g.Sum(t => Math.Abs(t.Amount)),
                TransactionCount = g.Count()
            })
            .OrderBy(m => m.Year)
            .ThenBy(m => m.Month)
            .ToList();

        var monthlyAscending = new List<MonthlySummaryDto>();

        for (int i = 0; i < monthlyRaw.Count; i++)
        {
            var current  = monthlyRaw[i];
            var previous = i > 0 ? monthlyRaw[i - 1] : null;

            decimal? change    = previous != null ? current.TotalSpent - previous.TotalSpent : null;
            decimal? changePct = previous?.TotalSpent > 0
                ? Math.Round(((current.TotalSpent - previous.TotalSpent) / previous.TotalSpent) * 100, 2)
                : null;

            monthlyAscending.Add(new MonthlySummaryDto
            {
                Year                             = current.Year,
                Month                            = current.Month,
                MonthName                        = new DateTime(current.Year, current.Month, 1).ToString("MMMM yyyy"),
                Total                            = current.TotalSpent,
                TransactionCount                 = current.TransactionCount,
                ChangeFromPreviousMonth          = change,
                PercentageChangeFromPreviousMonth = changePct
            });
        }

        var monthlyBreakdown = Enumerable.Reverse(monthlyAscending).ToList();

        var averageMonthlySpend = monthlyAscending.Count > 0
            ? Math.Round(monthlyAscending.Average(m => m.Total), 2)
            : 0;

        var biggestTransaction = expenseTransactions
            .OrderByDescending(t => Math.Abs(t.Amount))
            .FirstOrDefault();

        return new SpendingSummaryDto
        {
            TotalSpent              = totalSpent,
            TotalReceived           = totalReceived,
            TotalTransactionVolume  = totalTransactionVolume,
            TotalTransactions       = transactions.Count,
            AverageExpenseAmount    = expenseTransactions.Count > 0
                ? Math.Round(totalSpent / expenseTransactions.Count, 2)
                : 0,
            AverageMonthlySpend     = averageMonthlySpend,
            HighestSpendingCategory = categoryBreakdown.Count > 0
                ? categoryBreakdown.First().Category
                : "N/A",
            BiggestTransaction = biggestTransaction is null ? null : new TransactionDto
            {
                Id          = biggestTransaction.Id,
                Date        = biggestTransaction.Date,
                Description = biggestTransaction.Description,
                Amount      = Math.Abs(biggestTransaction.Amount),
                Category    = biggestTransaction.Category,
                IsCredit    = false,
                Type        = "Debit"
            },
            CategoryBreakdown = categoryBreakdown,
            MonthlyBreakdown  = monthlyBreakdown
        };
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Builds a stable, culture-invariant composite key for duplicate detection.
    ///
    /// Normalization applied:
    ///   - Date:        date portion only (yyyy-MM-dd), UTC-normalized
    ///   - Description: trimmed + collapsed internal whitespace + lower-invariant
    ///   - Amount:      Math.Abs to ensure sign doesn't create a false mismatch,
    ///                  formatted with InvariantCulture (G29) so the key is
    ///                  identical regardless of the server's locale setting
    ///
    /// The HashSet that consumes this key uses OrdinalIgnoreCase, so the
    /// ToLowerInvariant() call is redundant but adds a safety layer when keys
    /// are compared outside the HashSet (e.g. in logs).
    /// </summary>
    private static string MakeDuplicateKey(DateTime date, string description, decimal amount)
    {
        // Normalize date to UTC date-only string
        string dateKey = date.Date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        // Normalize description: trim edges, collapse internal runs of whitespace to a single space
        string descKey = System.Text.RegularExpressions.Regex
            .Replace((description ?? string.Empty).Trim(), @"\s+", " ")
            .ToLowerInvariant();

        // Normalize amount: always positive, culture-invariant decimal string
        string amountKey = Math.Abs(amount).ToString("G29", System.Globalization.CultureInfo.InvariantCulture);

        return $"{dateKey}|{descKey}|{amountKey}";
    }

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

    public async Task<int> DeleteAllTransactionsAsync(int userId)
    {
        _logger.LogInformation("Deleting all transactions for UserId={UserId}", userId);
        int deleted = await _transactionRepository.DeleteAllByUserIdAsync(userId);
        _logger.LogInformation("Deleted {Count} transactions for UserId={UserId}", deleted, userId);
        return deleted;
    }
}