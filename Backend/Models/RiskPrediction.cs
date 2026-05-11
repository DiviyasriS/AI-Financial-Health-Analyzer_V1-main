using System.ComponentModel.DataAnnotations.Schema;

/// <summary>
/// Stores the result of an ML risk prediction for a user.
/// One record is persisted per prediction run for auditability and trend tracking.
/// </summary>
public class RiskPrediction
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public DateTime PredictedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Raw confidence score from ML model (0.0 to 1.0).</summary>
    public float RiskScore { get; set; }

    /// <summary>Human-readable label: "Low", "Medium", or "High".</summary>
    public string RiskLevel { get; set; } = string.Empty;

    // ── Feature snapshot (for auditability + future retraining) ─────────────

    [Column(TypeName = "decimal(18,2)")]
    public decimal MonthlyAvgSpend { get; set; }

    public int TotalTransactions { get; set; }

    public int CategoryCount { get; set; }

    /// <summary>Top spending category at prediction time.</summary>
    public string TopCategory { get; set; } = string.Empty;

    /// <summary>Percentage of spend in the top category.</summary>
    [Column(TypeName = "decimal(5,2)")]
    public decimal TopCategoryPercentage { get; set; }

    /// <summary>Food spend as percentage of total.</summary>
    [Column(TypeName = "decimal(5,2)")]
    public decimal FoodSpendPercentage { get; set; }

    /// <summary>Entertainment spend as percentage of total.</summary>
    [Column(TypeName = "decimal(5,2)")]
    public decimal EntertainmentSpendPercentage { get; set; }

    /// <summary>Month-over-month spend change percentage.</summary>
    [Column(TypeName = "decimal(7,2)")]
    public decimal MoMSpendChangePercentage { get; set; }

    // Navigation property
    public User User { get; set; } = null!;
}