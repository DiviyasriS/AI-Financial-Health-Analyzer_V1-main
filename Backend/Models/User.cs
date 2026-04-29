public class User
{
    public int Id { get; set; }
    public string Email { get; set; }

    public List<Transaction> Transactions { get; set; }
}