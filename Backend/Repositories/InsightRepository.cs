using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

public class InsightRepository : IInsightRepository
{
    private readonly AppDbContext _context;
    private readonly ILogger<InsightRepository> _logger;

    public InsightRepository(
        AppDbContext context,
        ILogger<InsightRepository> logger)
    {
        _context = context;
        _logger  = logger;
    }

    public async Task SaveRangeAsync(List<Insight> insights)
    {
        if (insights.Count == 0) return;
        await _context.Insights.AddRangeAsync(insights);
        await _context.SaveChangesAsync();
        _logger.LogInformation("Saved {Count} insights.", insights.Count);
    }

    public async Task DeleteByUserIdAsync(int userId)
    {
        var existing = _context.Insights.Where(i => i.UserId == userId);
        _context.Insights.RemoveRange(existing);
        await _context.SaveChangesAsync();
    }

    public async Task<List<Insight>> GetByUserIdAsync(int userId)
    {
        return await _context.Insights
            .Where(i => i.UserId == userId)
            .OrderBy(i => i.Priority)
            .ToListAsync();
    }
}