public interface ITransactionRepository
{
    Task AddRangeAsync(List<Transaction> transactions);
    Task<List<Transaction>> GetByUserIdAsync(int userId);
    Task<List<Transaction>> GetByUserIdAndMonthAsync(int userId, int year, int month);

    /// <summary>
    /// Returns all unique user IDs that have at least one transaction.
    /// Used by the ML training pipeline to iterate over all users.
    /// </summary>
    Task<List<int>> GetAllUserIdsAsync();

    /// <summary>
    /// Fetches all transactions in a date range for batch duplicate checking.
    /// Replaces the N+1 DuplicateExistsAsync-per-row pattern.
    /// </summary>
    Task<List<Transaction>> GetByUserIdAndDateRangeAsync(int userId, DateTime startDate, DateTime endDate);

    /// <summary>Kept for backward compatibility — used in tests.</summary>
    Task<bool> DuplicateExistsAsync(int userId, DateTime date, string description, decimal amount);

    Task<int> GetTransactionCountByMonthAsync(int userId, int year, int month);
}