// All DTOs for spending analysis responses

public class SpendingSummaryDto
{
    // ── Totals ────────────────────────────────────────────────────────────────
    // TotalSpent  = sum of ALL debit/outgoing transactions (including transfers)
    // TotalReceived = sum of ALL credit/incoming transactions
    // TotalTransactionVolume = TotalSpent + TotalReceived
    public decimal TotalSpent             { get; set; }
    public decimal TotalReceived          { get; set; }
    public decimal TotalTransactionVolume { get; set; }

    public int     TotalTransactions      { get; set; }

    // Average amount per EXPENSE transaction (debits only)
    // Renamed from AverageTransactionAmount to be explicit
    public decimal AverageExpenseAmount   { get; set; }

    // ── Monthly averages ──────────────────────────────────────────────────────
    // Average monthly spend = average of each month's total debit sum
    public decimal AverageMonthlySpend    { get; set; }

    // ── Highlights ────────────────────────────────────────────────────────────
    // Highest spending category is based on ANALYTICS transactions only
    // (transfers and credits excluded)
    public string HighestSpendingCategory { get; set; } = string.Empty;
    public TransactionDto? BiggestTransaction { get; set; }

    // ── Breakdowns ────────────────────────────────────────────────────────────
    public List<CategorySummaryDto> CategoryBreakdown { get; set; } = new();
    public List<MonthlySummaryDto>  MonthlyBreakdown  { get; set; } = new();
}

public class CategorySummaryDto
{
    public string  Category          { get; set; } = string.Empty;
    public decimal Total             { get; set; }
    public int     TransactionCount  { get; set; }

    // Percentage of ANALYTICS total spend (transfers excluded)
    public decimal PercentageOfTotal { get; set; }

    // Top 3 transactions in this category
    public List<TransactionDto> TopTransactions { get; set; } = new();
}

public class MonthlySummaryDto
{
    public int    Year      { get; set; }
    public int    Month     { get; set; }
    public string MonthName { get; set; } = string.Empty;

    // Total is the sum of debit/expense transactions for this month only
    public decimal Total            { get; set; }
    public int     TransactionCount { get; set; }

    // Change vs the PREVIOUS calendar month (null if this is the first month)
    // Positive = spent more than previous month, Negative = spent less
    public decimal? ChangeFromPreviousMonth           { get; set; }
    public decimal? PercentageChangeFromPreviousMonth { get; set; }
}

public class TransactionDto
{
    public int      Id          { get; set; }
    public DateTime Date        { get; set; }
    public string   Description { get; set; } = string.Empty;
    public decimal  Amount      { get; set; }   // always positive (absolute value)
    public string   Category    { get; set; } = string.Empty;

    // Whether this transaction is a credit (money received) or debit (money spent)
    public bool   IsCredit { get; set; }
    public string Type     { get; set; } = string.Empty; // "Credit" or "Debit"
}