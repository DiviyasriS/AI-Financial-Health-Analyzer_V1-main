public interface IReportService
{
    Task<byte[]> GenerateFinancialReportPdfAsync(int userId);
}