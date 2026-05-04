using System.Globalization;
using Microsoft.Extensions.Logging;
// CsvService is responsible for ONE thing: parsing a CSV stream into Transaction objects
// It does NOT save to the database — saving is TransactionService's job
// It returns a ParsedFileResult so the caller knows exactly what happened row by row

public class CsvService
{
    private readonly ILogger<CsvService> _logger;

    public CsvService(ILogger<CsvService> logger)
{
    _logger = logger;
}
    public async Task<ParsedFileResult> ParseAsync(
        Stream fileStream, int userId, ITransactionRepository repository)
    {
        var result = new ParsedFileResult();

        using var reader = new StreamReader(fileStream);

        // Skip the header row
        var header = await reader.ReadLineAsync();
        if (header == null)
            return result; // completely empty file

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync();

            if (string.IsNullOrWhiteSpace(line))
                continue;

            result.TotalRowsFound++;

            // Handle CSV values that may contain commas inside quotes
            // e.g.  "Coffee Shop, Downtown",2024-01-15,5.50,Food
            var values = SplitCsvLine(line);

            if (values.Length < 3)
            {
                result.SkippedRows++;
                continue;
            }

            try
            {
                //var date        = DateTime.Parse(values[0].Trim());
                if (!DateTime.TryParse(values[0].Trim(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date))
{
                result.SkippedRows++;
                continue;
}
                var description = values[1].Trim();
                var amount      = decimal.Parse(values[2].Trim(), CultureInfo.InvariantCulture);
                var category    = values.Length > 3
                                    ? values[3].Trim()
                                    : "Uncategorized";

                // Skip blank descriptions
                if (string.IsNullOrWhiteSpace(description))
                {
                    result.SkippedRows++;
                    continue;
                }

                // Skip zero-amount rows
                if (amount == 0)
                {
                    result.SkippedRows++;
                    continue;
                }

                // Duplicate check against database
                var isDuplicate = await repository.DuplicateExistsAsync(
                    userId, date, description, amount);

                if (isDuplicate)
                {
                    result.DuplicateRows++;
                    continue;
                }

                result.Transactions.Add(new Transaction
                {
                    Date        = date,
                    Description = description,
                    Amount      = amount,
                    Category    = string.IsNullOrWhiteSpace(category)
                                    ? "Uncategorized"
                                    : category,
                    UserId      = userId
                });
            }
            catch (Exception ex)
{
    _logger.LogWarning("Skipping malformed CSV row: {Error}", ex.Message);
    result.SkippedRows++;
}
        }

        return result;
    }

    // ─── HELPER: Handle quoted CSV values containing commas ──────────────────

    private string[] SplitCsvLine(string line)
    {
        var values = new List<string>();
        var current = string.Empty;
        var insideQuotes = false;

        foreach (var ch in line)
        {
            if (ch == '"')
            {
                insideQuotes = !insideQuotes;
            }
            else if (ch == ',' && !insideQuotes)
            {
                values.Add(current.Trim());
                current = string.Empty;
            }
            else
            {
                current += ch;
            }
        }

        values.Add(current.Trim()); // add the last value
        return values.ToArray();
    }
}

// ─── INTERNAL RESULT MODEL ────────────────────────────────────────────────────
// Used only between CsvService/XlsxService and TransactionService
// Not exposed in API responses directly

public class ParsedFileResult
{
    public List<Transaction> Transactions { get; set; } = new();
    public int TotalRowsFound { get; set; }
    public int DuplicateRows { get; set; }
    public int SkippedRows { get; set; }
}