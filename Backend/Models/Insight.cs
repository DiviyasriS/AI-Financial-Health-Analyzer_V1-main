// Stores a single generated insight for a user
// Multiple insights can exist per user, refreshed on each analysis run

public class Insight
{
    public int Id { get; set; }

    public int UserId { get; set; }

    // Short title shown in the UI: "Overspending on Food"
    public string Title { get; set; } = string.Empty;

    // Detailed message explaining the insight
    public string Message { get; set; } = string.Empty;

    // Priority for ordering: 1 = highest priority
    public int Priority { get; set; }

    // Type tag: "warning", "info", "danger"
    public string Type { get; set; } = string.Empty;

    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
}