public class AuthProvider
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string ProviderName { get; set; } = string.Empty;
    public string ProviderUserId { get; set; } = string.Empty;
    public string? ProviderEmail { get; set; }
    public DateTime LinkedAtUtc { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
}
