public interface ITransactionRepository
{
    Task AddRangeAsync(List<Transaction> transactions);
    Task<List<Transaction>> GetByUserIdAsync(int userId);
    Task<bool> DuplicateExistsAsync(int userId, DateTime date, string description, decimal amount);
    Task<List<Transaction>> GetByUserIdAndMonthAsync(int userId, int year, int month);

    // NEW: Count how many transactions exist for a user in a given month
    // Used to detect if a monthly file has already been uploaded
    Task<int> GetTransactionCountByMonthAsync(int userId, int year, int month);
}