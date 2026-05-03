// Backend/Models/Transaction.cs  — REPLACE ENTIRE FILE
using System.ComponentModel.DataAnnotations;

public class Transaction
{
    public int Id { get; set; }

    [Required]
    public DateTime Date { get; set; }

    [Required]
    public string Description { get; set; } = string.Empty;  // add default

    [Required]
    public decimal Amount { get; set; }

    public string Category { get; set; } = string.Empty;  // add default

    public int UserId { get; set; }
}