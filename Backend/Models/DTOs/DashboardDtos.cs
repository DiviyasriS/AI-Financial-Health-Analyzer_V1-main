// DTOs returned by the Dashboard API endpoints

public class DashboardSummaryDto
{
    public decimal TotalSpent { get; set; }
    public decimal TotalReceived { get; set; }
    public decimal TotalTransactionVolume { get; set; }

    public int TotalTransactions { get; set; }
    public decimal AverageExpenseAmount { get; set; }
    public decimal AverageMonthlySpend { get; set; }
    public string HighestSpendingCategory { get; set; } = string.Empty;
    public TransactionDto? BiggestTransaction { get; set; }

    public List<CategorySummaryDto> CategoryBreakdown { get; set; } = new();
    public List<MonthlySummaryDto> MonthlyBreakdown { get; set; } = new();
}

public class RiskDto
{
    public string RiskLevel { get; set; } = string.Empty;

    // Financial risk severity from 0.0 to 1.0.
    // This is NOT ML confidence.
    public float RiskScore { get; set; }

    public DateTime PredictedAt { get; set; }

    // Friendly description for the UI
    public string Description { get; set; } = string.Empty;

    // Explainability fields for mentor/demo-ready output
    public List<string> RiskFactors { get; set; } = new();
    public List<string> PositiveSignals { get; set; } = new();
}

public class InsightDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public int Priority { get; set; }
    public string Type { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
}
