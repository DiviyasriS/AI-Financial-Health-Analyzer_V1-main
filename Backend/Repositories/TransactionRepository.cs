using Microsoft.EntityFrameworkCore;

public class TransactionRepository : ITransactionRepository
{
    private readonly AppDbContext _context;

    public TransactionRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task AddRangeAsync(List<Transaction> transactions)
    {
        if (transactions == null || transactions.Count == 0)
            return;

        await _context.Transactions.AddRangeAsync(transactions);
        await _context.SaveChangesAsync();
    }

    public async Task<List<Transaction>> GetByUserIdAsync(int userId)
    {
        return await _context.Transactions
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.Date)
            .ToListAsync();
    }

    public async Task<List<Transaction>> GetByUserIdAndMonthAsync(
        int userId, int year, int month)
    {
        return await _context.Transactions
            .Where(t => t.UserId == userId
                     && t.Date.Year  == year
                     && t.Date.Month == month)
            .OrderByDescending(t => t.Date)
            .ToListAsync();
    }

    /// <summary>
    /// Returns all distinct user IDs that have at least one transaction.
    /// Used by the ML training pipeline to build per-user feature vectors.
    /// </summary>
    public async Task<List<int>> GetAllUserIdsAsync()
    {
        return await _context.Transactions
            .Select(t => t.UserId)
            .Distinct()
            .ToListAsync();
    }

    public async Task<List<Transaction>> GetByUserIdAndDateRangeAsync(
        int userId, DateTime startDate, DateTime endDate)
    {
        return await _context.Transactions
            .Where(t => t.UserId == userId
                     && t.Date.Date >= startDate
                     && t.Date.Date <= endDate)
            .ToListAsync();
    }

    /// <summary>Kept for backward compatibility — still used in unit tests.</summary>
    public async Task<bool> DuplicateExistsAsync(
        int userId, DateTime date, string description, decimal amount)
    {
        return await _context.Transactions.AnyAsync(t =>
            t.UserId      == userId      &&
            t.Date        == date        &&
            t.Description == description &&
            t.Amount      == amount);
    }

    public async Task<int> GetTransactionCountByMonthAsync(
        int userId, int year, int month)
    {
        return await _context.Transactions
            .CountAsync(t => t.UserId      == userId
                          && t.Date.Year   == year
                          && t.Date.Month  == month);
    }
}