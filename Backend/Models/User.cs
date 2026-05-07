using System.Text.Json.Serialization;

public class User
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;

    [JsonIgnore] // Never serialize navigation property
    public List<Transaction> Transactions { get; set; } = new();
}