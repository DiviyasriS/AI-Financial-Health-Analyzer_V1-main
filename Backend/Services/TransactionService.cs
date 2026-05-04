public class TransactionService
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

    // ─── UPLOAD AND PROCESS FILE ──────────────────────────────────────────────

    public async Task<FileProcessingResultDto> ProcessAndSaveAsync(
        Stream fileStream, string fileName, int userId)
    {
        var extension = Path.GetExtension(fileName).ToLower().Trim();

        ParsedFileResult parsed;

        if (extension == ".csv")
            parsed = await _csvService.ParseAsync(fileStream, userId, _transactionRepository);
        else if (extension == ".xlsx" || extension == ".xls")
            parsed = await _xlsxService.ParseAsync(fileStream, userId, _transactionRepository);
        else
            throw new InvalidOperationException(
                $"Unsupported file type: {extension}.");

        // ── Monthly duplicate upload detection ────────────────────────────────
        // If all valid transactions fall in a single month, check if that month
        // already has data — warn the user but still allow it (soft block)
        string? monthWarning = null;

        if (parsed.Transactions.Count > 0)
        {
            var months = parsed.Transactions
                .Select(t => new { t.Date.Year, t.Date.Month })
                .Distinct()
                .ToList();

            // Only check when the entire file is for one month
            if (months.Count == 1)
            {
                var m = months.First();
                var existingCount = await _transactionRepository
                    .GetTransactionCountByMonthAsync(userId, m.Year, m.Month);

                if (existingCount > 0)
                {
                    var monthName = new DateTime(m.Year, m.Month, 1)
                        .ToString("MMMM yyyy");
                    monthWarning =
                        $"Warning: {existingCount} transactions for {monthName} " +
                        $"already exist. Duplicates were skipped automatically.";
                }
            }
        }

        await _transactionRepository.AddRangeAsync(parsed.Transactions);

        return new FileProcessingResultDto
        {
            SavedCount     = parsed.Transactions.Count,
            DuplicateCount = parsed.DuplicateRows,
            SkippedCount   = parsed.SkippedRows,
            TotalRowsFound = parsed.TotalRowsFound,
            FileType       = extension.TrimStart('.').ToUpper(),
            Message        = BuildSummaryMessage(parsed, extension),
            MonthWarning   = monthWarning
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
                TotalSpent             = 0,
                TotalTransactions      = 0,
                AverageMonthlySpend    = 0,
                AverageTransactionAmount = 0,
                HighestSpendingCategory = "N/A",
                CategoryBreakdown      = new List<CategorySummaryDto>(),
                MonthlyBreakdown       = new List<MonthlySummaryDto>()
            };
        }

        var totalSpent = transactions.Sum(t => t.Amount);

        // ── Category breakdown with percentage + top transactions ─────────────
        var categoryBreakdown = transactions
            .GroupBy(t => t.Category)
            .Select(g =>
            {
                var categoryTotal = g.Sum(t => t.Amount);

                return new CategorySummaryDto
                {
                    Category         = g.Key,
                    Total            = categoryTotal,
                    TransactionCount = g.Count(),

                    // Round to 2 decimal places for clean display
                    PercentageOfTotal = totalSpent > 0
                        ? Math.Round((categoryTotal / totalSpent) * 100, 2)
                        : 0,

                    // Top 3 transactions in this category by amount
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

        // ── Monthly breakdown with month-over-month change ────────────────────
        var monthlyRaw = transactions
            .GroupBy(t => new { t.Date.Year, t.Date.Month })
            .Select(g => new
            {
                g.Key.Year,
                g.Key.Month,
                Total            = g.Sum(t => t.Amount),
                TransactionCount = g.Count()
            })
            .OrderBy(m => m.Year)
            .ThenBy(m => m.Month)
            .ToList();

        var monthlyBreakdown = new List<MonthlySummaryDto>();

        for (int i = 0; i < monthlyRaw.Count; i++)
        {
            var current  = monthlyRaw[i];
            var previous = i > 0 ? monthlyRaw[i - 1] : null;

            decimal? change     = null;
            decimal? changePct  = null;

            if (previous != null)
            {
                change    = current.Total - previous.Total;
                changePct = previous.Total > 0
                    ? Math.Round(((current.Total - previous.Total) / previous.Total) * 100, 2)
                    : null;
            }

            monthlyBreakdown.Add(new MonthlySummaryDto
            {
                Year             = current.Year,
                Month            = current.Month,
                MonthName        = new DateTime(current.Year, current.Month, 1)
                                       .ToString("MMMM yyyy"),
                Total            = current.Total,
                TransactionCount = current.TransactionCount,
                ChangeFromPreviousMonth           = change,
                PercentageChangeFromPreviousMonth = changePct
            });
        }

        // Reverse so most recent month appears first in the response
        monthlyBreakdown.Reverse();

        // ── Highlights ────────────────────────────────────────────────────────
        var biggestTransaction = transactions
            .OrderByDescending(t => t.Amount)
            .First();

        var highestCategory = categoryBreakdown.First().Category;

        // Average monthly spend across all months that have data
        var avgMonthlySpend = monthlyBreakdown.Count > 0
            ? Math.Round(monthlyBreakdown.Average(m => m.Total), 2)
            : 0;

        return new SpendingSummaryDto
        {
            TotalSpent               = totalSpent,
            TotalTransactions        = transactions.Count,
            AverageTransactionAmount = Math.Round(totalSpent / transactions.Count, 2),
            AverageMonthlySpend      = avgMonthlySpend,
            HighestSpendingCategory  = highestCategory,

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