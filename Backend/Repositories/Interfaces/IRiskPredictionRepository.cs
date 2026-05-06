public interface IRiskPredictionRepository
{
    Task SaveAsync(RiskPrediction prediction);
    Task<RiskPrediction?> GetLatestByUserIdAsync(int userId);
}