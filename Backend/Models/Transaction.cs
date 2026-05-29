// Backend/Models/Transaction.cs 
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;  

public class Transaction
{
    public int Id { get; set; }

    [Required]
    public DateTime Date { get; set; }

    [Required]
    public string Description { get; set; } = string.Empty;  // add default

    [Required]
    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }

    public string Category { get; set; } = string.Empty;  // add default

    public bool IsCredit { get; set; } = false;  // true = money received, false = money sent


    public int UserId { get; set; }
}