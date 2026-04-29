using System.ComponentModel.DataAnnotations;

public class Transaction
{
    public int Id { get; set; }

    [Required]
    public DateTime Date { get; set; }

    [Required]
    public string Description { get; set; }

    [Required]
    public decimal Amount { get; set; }

    public string Category { get; set; }

    public int UserId { get; set; }
}