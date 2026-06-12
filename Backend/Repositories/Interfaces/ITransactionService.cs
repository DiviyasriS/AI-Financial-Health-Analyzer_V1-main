public interface ITransactionService
{
    Task<FileProcessingResultDto> ProcessAndSaveAsync(Stream fileStream, string fileName, int userId);
    Task<List<TransactionDto>> GetTransactionsAsync(int userId);
    Task<SpendingSummaryDto> GetSummaryAsync(int userId);

    /// <summary>
    /// Permanently deletes all transactions for the specified user.
    /// Returns the count of deleted records.
    /// Used to allow users to clear incorrectly parsed data and re-upload.
    /// </summary>
    Task<int> DeleteAllTransactionsAsync(int userId);
}