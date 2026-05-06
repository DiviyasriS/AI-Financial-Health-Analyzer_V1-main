// Stores the result of an ML risk prediction for a user
// One record per prediction run — we keep history for auditability

public class RiskPrediction
{
    public int Id { get; set; }

    public int UserId { get; set; }

    // When the prediction was generated
    public DateTime PredictedAt { get; set; } = DateTime.UtcNow;

    // Raw score from ML model (0.0 to 1.0)
    public float RiskScore { get; set; }

    // Human-readable label: "Low", "Medium", "High"
    public string RiskLevel { get; set; } = string.Empty;

    // Snapshot inputs used for this prediction (for traceability)
    public decimal MonthlyAvgSpend { get; set; }
    public int TotalTransactions { get; set; }
    public int CategoryCount { get; set; }

    // Navigation property
    public User User { get; set; } = null!;
}