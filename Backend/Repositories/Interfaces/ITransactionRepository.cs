// Defines WHAT operations are available for transactions
// The Service layer only ever depends on this interface — never the concrete class
// This keeps the code testable and the layers properly separated

public interface ITransactionRepository
{
    // Save a list of processed transactions to the database
    Task AddRangeAsync(List<Transaction> transactions);

    // Get all transactions belonging to a specific user
    Task<List<Transaction>> GetByUserIdAsync(int userId);

    // Check if an identical transaction already exists (duplicate detection)
    Task<bool> DuplicateExistsAsync(int userId, DateTime date, string description, decimal amount);

    // Get all transactions for a user within a specific month
    Task<List<Transaction>> GetByUserIdAndMonthAsync(int userId, int year, int month);
}