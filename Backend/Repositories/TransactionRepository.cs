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

    // FIX: Added GetByUserIdAndDateRangeAsync to support batch duplicate checking.
    // TransactionService previously called DuplicateExistsAsync once per row (N+1 pattern).
    // Now TransactionService fetches all transactions in the CSV's date range in one query
    // and does duplicate detection in-memory with a HashSet.
    public async Task<List<Transaction>> GetByUserIdAndDateRangeAsync(
        int userId, DateTime startDate, DateTime endDate)
    {
        return await _context.Transactions
            .Where(t => t.UserId == userId
                     && t.Date.Date >= startDate
                     && t.Date.Date <= endDate)
            .ToListAsync();
    }

    // Kept for backward compatibility — still used in tests
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