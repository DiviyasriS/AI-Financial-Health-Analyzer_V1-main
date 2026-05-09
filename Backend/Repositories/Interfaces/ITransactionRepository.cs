public interface ITransactionRepository
{
    Task AddRangeAsync(List<Transaction> transactions);
    Task<List<Transaction>> GetByUserIdAsync(int userId);
    Task<List<Transaction>> GetByUserIdAndMonthAsync(int userId, int year, int month);

    // FIX: New method — fetches all transactions in a date range for batch duplicate checking.
    // Replaces the N+1 DuplicateExistsAsync-per-row pattern in TransactionService.
    Task<List<Transaction>> GetByUserIdAndDateRangeAsync(int userId, DateTime startDate, DateTime endDate);

    // Kept for backward compatibility (used in tests)
    Task<bool> DuplicateExistsAsync(int userId, DateTime date, string description, decimal amount);

    Task<int> GetTransactionCountByMonthAsync(int userId, int year, int month);
}