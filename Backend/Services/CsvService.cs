using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

// CsvService is responsible for ONE thing: parsing a CSV stream into Transaction objects.
// It does NOT save to the database — saving is TransactionService's job.
//
// FIX: SplitCsvLine now uses StringBuilder instead of string concatenation.


public class CsvService
{
    private readonly ILogger<CsvService> _logger;

    public CsvService(ILogger<CsvService> logger)
    {
        _logger = logger;
    }

    public async Task<ParsedFileResult> ParseAsync(Stream fileStream, int userId)
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

            var values = SplitCsvLine(line);

            if (values.Length < 3)
            {
                _logger.LogDebug("Skipping CSV row with fewer than 3 columns");
                result.SkippedRows++;
                continue;
            }

            try
            {
                if (!DateTime.TryParse(
    values[0].Trim(),
    CultureInfo.InvariantCulture,
    DateTimeStyles.AssumeLocal,
    out DateTime date))
{
    _logger.LogDebug("Skipping row — cannot parse date: '{Value}'", values[0]);
    result.SkippedRows++;
    continue;
}

date = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc);

                var description = values[1].Trim();
                if (string.IsNullOrWhiteSpace(description))
                {
                    result.SkippedRows++;
                    continue;
                }

                if (!decimal.TryParse(
                    values[2].Trim(),
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out var amount))
                {
                    _logger.LogDebug("Skipping row — cannot parse amount: '{Value}'", values[2]);
                    result.SkippedRows++;
                    continue;
                }

                if (amount == 0)
                {
                    result.SkippedRows++;
                    continue;
                }

                var category = values.Length > 3 && !string.IsNullOrWhiteSpace(values[3])
                    ? values[3].Trim()
                    : "Uncategorized";

                result.Transactions.Add(new Transaction
                {
                    Date        = date,
                    Description = description,
                    Amount      = amount,
                    Category    = category,
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

    // FIX: Use StringBuilder — old code used += char which is O(n²) allocation.
    private static string[] SplitCsvLine(string line)
    {
        var values      = new List<string>();
        var current     = new StringBuilder();
        var insideQuotes = false;

        foreach (var ch in line)
        {
            if (ch == '"')
            {
                insideQuotes = !insideQuotes;
            }
            else if (ch == ',' && !insideQuotes)
            {
                values.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        values.Add(current.ToString().Trim()); // last value
        return values.ToArray();
    }
}

// ─── INTERNAL RESULT MODEL ────────────────────────────────────────────────────

public class ParsedFileResult
{
    public List<Transaction> Transactions { get; set; } = new();
    public int TotalRowsFound { get; set; }
    public int DuplicateRows  { get; set; }
    public int SkippedRows    { get; set; }
}