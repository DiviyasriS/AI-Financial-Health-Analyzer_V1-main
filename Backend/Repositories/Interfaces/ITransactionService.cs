public interface ITransactionService
{
    Task<FileProcessingResultDto> ProcessAndSaveAsync(Stream fileStream, string fileName, int userId);
    Task<List<TransactionDto>> GetTransactionsAsync(int userId);
    Task<SpendingSummaryDto> GetSummaryAsync(int userId);
}