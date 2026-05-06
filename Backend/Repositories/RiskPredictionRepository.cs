using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

public class RiskPredictionRepository : IRiskPredictionRepository
{
    private readonly AppDbContext _context;
    private readonly ILogger<RiskPredictionRepository> _logger;

    public RiskPredictionRepository(
        AppDbContext context,
        ILogger<RiskPredictionRepository> logger)
    {
        _context = context;
        _logger  = logger;
    }

    public async Task SaveAsync(RiskPrediction prediction)
    {
        _context.RiskPredictions.Add(prediction);
        await _context.SaveChangesAsync();
        _logger.LogInformation(
            "Saved risk prediction for UserId={UserId}: {Level}",
            prediction.UserId, prediction.RiskLevel);
    }

    public async Task<RiskPrediction?> GetLatestByUserIdAsync(int userId)
    {
        return await _context.RiskPredictions
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.PredictedAt)
            .FirstOrDefaultAsync();
    }
}