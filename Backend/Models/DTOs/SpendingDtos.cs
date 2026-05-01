// All DTOs for spending analysis responses

public class SpendingSummaryDto
{
    // ── Totals ────────────────────────────────────────────────────────────────
    public decimal TotalSpent { get; set; }
    public int TotalTransactions { get; set; }
    public decimal AverageTransactionAmount { get; set; }

    // ── Monthly averages ──────────────────────────────────────────────────────
    public decimal AverageMonthlySpend { get; set; }

    // ── Highlights ────────────────────────────────────────────────────────────
    public string HighestSpendingCategory { get; set; } = string.Empty;
    public TransactionDto? BiggestTransaction { get; set; }

    // ── Breakdowns ────────────────────────────────────────────────────────────
    public List<CategorySummaryDto> CategoryBreakdown { get; set; } = new();
    public List<MonthlySummaryDto> MonthlyBreakdown { get; set; } = new();
}

public class CategorySummaryDto
{
    public string Category { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public int TransactionCount { get; set; }

    // What percentage of total spend this category represents
    // e.g. 35.4 means 35.4%
    public decimal PercentageOfTotal { get; set; }

    // Top 3 transactions in this category
    public List<TransactionDto> TopTransactions { get; set; } = new();
}

public class MonthlySummaryDto
{
    public int Year { get; set; }
    public int Month { get; set; }
    public string MonthName { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public int TransactionCount { get; set; }

    // How much more or less than the previous month
    // Positive = spent more, Negative = spent less, null = no previous month
    public decimal? ChangeFromPreviousMonth { get; set; }

    // Percentage change from previous month
    // null if no previous month to compare against
    public decimal? PercentageChangeFromPreviousMonth { get; set; }
}

public class TransactionDto
{
    public int Id { get; set; }
    public DateTime Date { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Category { get; set; } = string.Empty;
}