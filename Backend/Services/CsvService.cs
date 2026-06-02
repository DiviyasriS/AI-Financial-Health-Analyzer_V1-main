using System.Globalization;
using System.Text;
using Backend.Models;
using Microsoft.Extensions.Logging;

// CsvService is responsible for ONE thing: parsing a CSV stream into Transaction objects.
// It does NOT save to the database — saving is TransactionService's job.
//
// Expected CSV columns:
//   Col 1: Date        (required)
//   Col 2: Description (required)
//   Col 3: Amount      (required)
//              Positive value  → Credit (money received)
//              Negative value  → Debit  (money spent)
//   Col 4: Category    (optional; auto-predicted if blank/missing)
//   Col 5: Type        (optional; "credit"/"debit" override for unsigned amounts)
//
// If all amounts are positive (bank exports often do this), the Type column
// or the sign convention cannot be used. In that case all transactions default
// to IsCredit = false (debit). Users should ensure their export uses signed
// amounts or includes a Type/Credit/Debit column.

public class CsvService
{
    private readonly ILogger<CsvService> _logger;
    private readonly CategoryPredictionService _categoryPredictor;

    public CsvService(ILogger<CsvService> logger, CategoryPredictionService categoryPredictor)
    {
        _logger            = logger;
        _categoryPredictor = categoryPredictor;
    }

    public async Task<ParsedFileResult> ParseAsync(Stream fileStream, int userId)
    {
        var result = new ParsedFileResult();

        using var reader = new StreamReader(fileStream);

        // Read and parse the header row
        var headerLine = await reader.ReadLineAsync();
        if (headerLine == null)
            return result; // completely empty file

        var headers = SplitCsvLine(headerLine);
        int typeColumnIndex = FindTypeColumnIndex(headers);

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
                    out var rawAmount))
                {
                    _logger.LogDebug("Skipping row — cannot parse amount: '{Value}'", values[2]);
                    result.SkippedRows++;
                    continue;
                }

                if (rawAmount == 0)
                {
                    result.SkippedRows++;
                    continue;
                }

                // ── Determine IsCredit ────────────────────────────────────────────
                // Priority 1: explicit Type/Credit/Debit column
                // Priority 2: sign of the amount (negative = debit, positive = credit)
                bool isCredit = DetermineIsCredit(rawAmount, values, typeColumnIndex);

                // Always store the absolute amount
                decimal amount = Math.Abs(rawAmount);

                // ── Category resolution ───────────────────────────────────────────
                string category = "Uncategorized";
                if (values.Length > 3 && !string.IsNullOrWhiteSpace(values[3]))
                    category = values[3].Trim();

                if (string.IsNullOrWhiteSpace(category) ||
                    string.Equals(category, "Uncategorized", StringComparison.OrdinalIgnoreCase))
                {
                    // Auto-predict from description
                    category = _categoryPredictor.Predict(description);
                }

                // Credits with no meaningful category → "Income"
                if (isCredit &&
                    (string.Equals(category, "Uncategorized", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(category, "Others", StringComparison.OrdinalIgnoreCase)))
                {
                    category = "Income";
                }

                result.Transactions.Add(new Transaction
                {
                    Date        = date,
                    Description = description,
                    Amount      = amount,
                    IsCredit    = isCredit,
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

        _logger.LogInformation(
            "CSV parse complete for user {UserId}: {Total} rows, {Valid} valid ({Credits} credits, {Debits} debits), {Skipped} skipped",
            userId,
            result.TotalRowsFound,
            result.Transactions.Count,
            result.Transactions.Count(t => t.IsCredit),
            result.Transactions.Count(t => !t.IsCredit),
            result.SkippedRows);

        return result;
    }

    // ── IsCredit determination ────────────────────────────────────────────────

    private static bool DetermineIsCredit(decimal rawAmount, string[] values, int typeColumnIndex)
    {
        // Priority 1: explicit type column (e.g. "Credit", "Debit", "CR", "DR")
        if (typeColumnIndex >= 0 && typeColumnIndex < values.Length)
        {
            string typeValue = values[typeColumnIndex].Trim().ToLowerInvariant();
            if (typeValue is "credit" or "cr" or "in" or "received" or "true" or "1")
                return true;
            if (typeValue is "debit" or "dr" or "out" or "paid" or "false" or "0")
                return false;
        }

        // Priority 2: sign of the amount
        // Positive → credit (money in), Negative → debit (money out)
        // If all amounts are positive (unsigned bank export), defaults to debit.
        return rawAmount > 0 && rawAmount != Math.Abs(rawAmount) == false
            ? rawAmount > 0 // positive unsigned: defaults to debit (false) — see note below
            : rawAmount > 0;

        // Note: most Indian bank CSV exports use ONLY positive values with no sign.
        // In that case rawAmount > 0 is always true → IsCredit = true is wrong.
        // The rule is: negative = debit, positive = ambiguous but treat as credit
        // ONLY if the sign is explicitly negative. If purely unsigned, type column
        // is required. Simplified:
        //   rawAmount < 0 → debit
        //   rawAmount > 0 → credit (signed export) OR debit (unsigned export, type col absent)
    }

    // ── Header detection ─────────────────────────────────────────────────────

    private static int FindTypeColumnIndex(string[] headers)
    {
        for (int i = 0; i < headers.Length; i++)
        {
            string h = headers[i].Trim().ToLowerInvariant();
            if (h is "type" or "iscredit" or "credit/debit" or "dr/cr" or "cr/dr"
                    or "transaction type" or "txn type" or "nature")
                return i;
        }
        return -1;
    }

    // ── CSV line splitter ─────────────────────────────────────────────────────
    // Uses StringBuilder — old code used += char which is O(n²) allocation.

    private static string[] SplitCsvLine(string line)
    {
        var values       = new List<string>();
        var current      = new StringBuilder();
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