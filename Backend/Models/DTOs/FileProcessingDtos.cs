// DTOs specifically for file upload responses
// Gives the frontend detailed information about what happened during processing

public class FileProcessingResultDto
{
    // How many transactions were successfully saved
    public int SavedCount { get; set; }

    // How many rows were skipped because they already exist
    public int DuplicateCount { get; set; }

    // How many rows were skipped because they were malformed
    public int SkippedCount { get; set; }

    // Total rows found in the file (excluding header)
    public int TotalRowsFound { get; set; }

    // Human readable summary message
    public string Message { get; set; } = string.Empty;

    // Which file type was processed
    public string FileType { get; set; } = string.Empty;
}