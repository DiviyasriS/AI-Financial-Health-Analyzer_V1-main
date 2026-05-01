using Microsoft.EntityFrameworkCore;

// Concrete implementation of ITransactionRepository
// This is the ONLY place in the codebase that touches the database for transactions
// All EF Core queries for transactions live here — nowhere else

public class TransactionRepository : ITransactionRepository
{
    private readonly AppDbContext _context;

    public TransactionRepository(AppDbContext context)
    {
        _context = context;
    }

    // ─── SAVE ────────────────────────────────────────────────────────────────

    public async Task AddRangeAsync(List<Transaction> transactions)
    {
        if (transactions == null || transactions.Count == 0)
            return;

        await _context.Transactions.AddRangeAsync(transactions);
        await _context.SaveChangesAsync();
    }

    // ─── READ ─────────────────────────────────────────────────────────────────

    public async Task<List<Transaction>> GetByUserIdAsync(int userId)
    {
        return await _context.Transactions
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.Date)
            .ToListAsync();
    }

    public async Task<List<Transaction>> GetByUserIdAndMonthAsync(int userId, int year, int month)
    {
        return await _context.Transactions
            .Where(t => t.UserId == userId
                     && t.Date.Year == year
                     && t.Date.Month == month)
            .OrderByDescending(t => t.Date)
            .ToListAsync();
    }

    // ─── DUPLICATE CHECK ──────────────────────────────────────────────────────

    public async Task<bool> DuplicateExistsAsync(
        int userId, DateTime date, string description, decimal amount)
    {
        return await _context.Transactions.AnyAsync(t =>
            t.UserId == userId &&
            t.Date == date &&
            t.Description == description &&
            t.Amount == amount);
    }
}