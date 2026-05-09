using System.ComponentModel.DataAnnotations.Schema;

// Stores the result of an ML risk prediction for a user.
// One record per prediction run — kept for auditability.

public class RiskPrediction
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public DateTime PredictedAt { get; set; } = DateTime.UtcNow;

    // Raw score from ML model (0.0 to 1.0)
    public float RiskScore { get; set; }

    // Human-readable label: "Low", "Medium", "High"
    public string RiskLevel { get; set; } = string.Empty;

    [Column(TypeName = "decimal(18,2)")]
    public decimal MonthlyAvgSpend { get; set; }

    public int TotalTransactions { get; set; }

    public int CategoryCount { get; set; }

    // Navigation property
    public User User { get; set; } = null!;
}