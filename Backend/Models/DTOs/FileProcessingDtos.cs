using Backend.Models;

public class ParsedFileResult
{
    public List<Transaction> Transactions { get; set; } = new();
    public int TotalRowsFound { get; set; }
    public int SkippedRows { get; set; }
    public int DuplicateRows { get; set; }
}

public class FileProcessingResultDto
{
    public int SavedCount { get; set; }
    public int DuplicateCount { get; set; }
    public int SkippedCount { get; set; }
    public int TotalRowsFound { get; set; }
    public string Message { get; set; } = string.Empty;
    public string FileType { get; set; } = string.Empty;

    // Populated when uploading data for a month that already has transactions
    // null means no warning — this is a clean first upload for those months
    public string? MonthWarning { get; set; }
}