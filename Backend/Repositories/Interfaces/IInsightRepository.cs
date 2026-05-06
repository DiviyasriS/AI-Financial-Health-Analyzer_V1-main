public interface IInsightRepository
{
    Task SaveRangeAsync(List<Insight> insights);
    Task DeleteByUserIdAsync(int userId);
    Task<List<Insight>> GetByUserIdAsync(int userId);
}