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
                     && t.Date.Year == year
                     && t.Date.Month == month)
            .OrderByDescending(t => t.Date)
            .ToListAsync();
    }

    public async Task<bool> DuplicateExistsAsync(
        int userId, DateTime date, string description, decimal amount)
    {
        return await _context.Transactions.AnyAsync(t =>
            t.UserId == userId &&
            t.Date == date &&
            t.Description == description &&
            t.Amount == amount);
    }

    // ─── NEW ──────────────────────────────────────────────────────────────────
    // Returns how many transactions exist for a user in a given year+month
    // If this returns > 0, we know data for that month was already uploaded

    public async Task<int> GetTransactionCountByMonthAsync(
        int userId, int year, int month)
    {
        return await _context.Transactions
            .CountAsync(t => t.UserId == userId
                          && t.Date.Year == year
                          && t.Date.Month == month);
    }
}