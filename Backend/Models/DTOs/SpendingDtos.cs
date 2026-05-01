// DTOs for spending analysis responses
// These are what the API returns — clean, flat objects designed for the frontend

public class SpendingSummaryDto
{
    public decimal TotalSpent { get; set; }
    public List<CategorySummaryDto> CategoryBreakdown { get; set; } = new();
    public List<MonthlySummaryDto> MonthlyBreakdown { get; set; } = new();
}

public class CategorySummaryDto
{
    // The category name (e.g. "Food", "Transport", "Utilities")
    public string Category { get; set; } = string.Empty;

    // Total amount spent in this category
    public decimal Total { get; set; }

    // How many transactions fall in this category
    public int TransactionCount { get; set; }
}

public class MonthlySummaryDto
{
    public int Year { get; set; }
    public int Month { get; set; }

    // Human readable label — makes it easier on the frontend
    public string MonthName { get; set; } = string.Empty;

    public decimal Total { get; set; }
    public int TransactionCount { get; set; }
}

// Used when returning a single transaction in API responses
public class TransactionDto
{
    public int Id { get; set; }
    public DateTime Date { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Category { get; set; } = string.Empty;
}