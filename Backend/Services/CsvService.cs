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
//   Col 4: Category    (optional; auto-predicted if blank/missing)
//   Col 5: Type        (optional; "credit"/"debit" override)
//
// DIRECTION DETECTION — three-tier priority:
//
//   TIER 1 — Explicit type column: "Credit", "CR", "Debit", "DR", etc.
//             Present in some bank exports and always takes precedence.
//
//   TIER 2 — Signed amounts: if ANY amount in the file is negative, the export
//             uses sign convention (negative = debit, positive = credit).
//             This is the standard for European / international bank exports.
//
//   TIER 3 — Unsigned export (all amounts positive, no type column): the safe
//             assumption is IsCredit = false (debit) for every row.
//             This is correct for most Indian bank CSV exports (HDFC, ICICI,
//             SBI, Axis) which export all amounts as positive numbers.
//             Users who need accurate credit classification should use an export
//             that includes a Type/Credit/Debit column.
//
// IMPORTANT: the previous implementation contained a logical error where the
// sign-detection ternary evaluated identically on both branches:
//
//   BAD:  return rawAmount > 0 && rawAmount != Math.Abs(rawAmount) == false
//             ? rawAmount > 0
//             : rawAmount > 0;
//   This always returns (rawAmount > 0), misclassifying every unsigned debit
//   as a credit and making them invisible to spending analysis.
//
// FIXED: we now detect the export type up front (signed vs unsigned) by
// scanning buffered lines before parsing, then apply the correct rule.

public class CsvService
{
    private readonly ILogger<CsvService> _logger;
    private readonly CategoryPredictionService _categoryPredictor;

    public CsvService(ILogger<CsvService> logger, CategoryPredictionService categoryPredictor)
    {
        _logger            = logger;
        _categoryPredictor = categoryPredictor;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Parses a CSV stream and returns all valid <see cref="Transaction"/> objects.
    /// Uses a two-pass strategy: buffer all lines, detect signed vs unsigned export,
    /// then classify each row with the correct direction rule.
    /// </summary>
    public async Task<ParsedFileResult> ParseAsync(Stream fileStream, int userId)
    {
        var result = new ParsedFileResult();

        using var reader = new StreamReader(fileStream);

        // ── Pass 1: read header ───────────────────────────────────────────────
        var headerLine = await reader.ReadLineAsync();
        if (headerLine == null)
            return result; // completely empty file

        var headers         = SplitCsvLine(headerLine);
        int typeColumnIndex = FindTypeColumnIndex(headers);

        // ── Pass 1 continued: buffer all data lines ───────────────────────────
        // Bank statements are small (< 5 MB), so buffering is safe and lets us
        // scan for negative amounts before parsing direction.
        var dataLines = new List<string>();
        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync();
            if (!string.IsNullOrWhiteSpace(line))
                dataLines.Add(line);
        }

        // ── Pass 1 completed: detect export format ────────────────────────────
        bool isSignedExport = DetectSignedExport(dataLines);

        _logger.LogInformation(
            "CSV format detection for user {UserId}: TypeColumnIndex={TypeCol}, IsSignedExport={Signed}, TotalDataLines={Lines}",
            userId, typeColumnIndex, isSignedExport, dataLines.Count);

        // ── Pass 2: parse each data line ──────────────────────────────────────
        foreach (var line in dataLines)
        {
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
                // ── Date ──────────────────────────────────────────────────────
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

                // ── Description ───────────────────────────────────────────────
                var description = values[1].Trim();
                if (string.IsNullOrWhiteSpace(description))
                {
                    result.SkippedRows++;
                    continue;
                }

                // ── Amount ────────────────────────────────────────────────────
                // Strip currency symbols and thousands separators (e.g. ₹1,23,456.78)
                string amountStr = StripCurrencyFormatting(values[2].Trim());

                if (!decimal.TryParse(
                    amountStr,
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

                // ── Direction (IsCredit) ───────────────────────────────────────
                bool isCredit = DetermineIsCredit(rawAmount, values, typeColumnIndex, isSignedExport);

                // Always store the absolute amount; sign is captured in IsCredit
                decimal amount = Math.Abs(rawAmount);

                // ── Category ──────────────────────────────────────────────────
                string category = "Uncategorized";
                if (values.Length > 3 && !string.IsNullOrWhiteSpace(values[3]))
                    category = values[3].Trim();

                if (string.IsNullOrWhiteSpace(category) ||
                    string.Equals(category, "Uncategorized", StringComparison.OrdinalIgnoreCase))
                {
                    category = _categoryPredictor.Predict(description);
                }

                // Credits with no meaningful category → classify as "Income"
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

    // ── Direction detection ───────────────────────────────────────────────────

    /// <summary>
    /// Determines whether a transaction row represents money received (IsCredit = true)
    /// or money spent (IsCredit = false).
    ///
    /// Decision hierarchy:
    ///
    ///   1. Explicit type column — always wins.
    ///      Values recognised as credit: "credit", "cr", "in", "received", "true", "1"
    ///      Values recognised as debit:  "debit",  "dr", "out", "paid", "false", "0"
    ///      Unknown type value → fall through.
    ///
    ///   2. Signed export (isSignedExport == true):
    ///      rawAmount &lt; 0 → Debit  (outgoing money)
    ///      rawAmount &gt; 0 → Credit (incoming money)
    ///
    ///   3. Unsigned export (isSignedExport == false, no type column):
    ///      All amounts are positive; no direction information available.
    ///      → Default to Debit (IsCredit = false).
    ///      This is the correct conservative assumption for the majority of
    ///      Indian bank statement exports.
    /// </summary>
    private static bool DetermineIsCredit(
        decimal rawAmount,
        string[] values,
        int typeColumnIndex,
        bool isSignedExport)
    {
        // Tier 1: explicit type column
        if (typeColumnIndex >= 0 && typeColumnIndex < values.Length)
        {
            string typeValue = values[typeColumnIndex].Trim().ToLowerInvariant();
            if (typeValue is "credit" or "cr" or "in" or "received" or "true" or "1")
                return true;
            if (typeValue is "debit" or "dr" or "out" or "paid" or "false" or "0")
                return false;
            // Unknown type string — fall through to sign-based logic
        }

        // Tier 2: signed export
        if (isSignedExport)
        {
            // Negative = debit (money out), positive = credit (money in)
            return rawAmount > 0;
        }

        // Tier 3: unsigned export with no type column → conservatively treat as debit
        return false;
    }

    // ── Signed-export detector ────────────────────────────────────────────────

    /// <summary>
    /// Scans up to 50 buffered data lines to determine whether the file uses signed amounts.
    /// A single negative amount value is sufficient to classify the file as a signed export.
    /// </summary>
    private static bool DetectSignedExport(List<string> dataLines)
    {
        int rowsChecked = 0;

        foreach (var line in dataLines)
        {
            if (rowsChecked >= 50) break;

            var values = SplitCsvLine(line);
            if (values.Length < 3)
                continue;

            string amountStr = StripCurrencyFormatting(values[2].Trim());

            if (decimal.TryParse(
                amountStr,
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out decimal amt))
            {
                if (amt < 0)
                    return true; // confirmed signed export
            }

            rowsChecked++;
        }

        return false; // all sampled amounts were positive → unsigned export
    }

    // ── Currency formatting stripper ──────────────────────────────────────────

    /// <summary>
    /// Removes currency symbols (₹ $ € £) and thousands/lakh separators so that
    /// values like "₹1,23,456.78" or "$1,234.56" can be parsed by decimal.TryParse.
    /// </summary>
    private static string StripCurrencyFormatting(string raw)
    {
        return raw
            .Replace("₹", "")
            .Replace("$", "")
            .Replace("€", "")
            .Replace("£", "")
            .Replace(",", "") // removes both Western (1,234) and Indian (1,23,456) thousands separators
            .Trim();
    }

    // ── Header detection ─────────────────────────────────────────────────────

    /// <summary>
    /// Searches header columns for a type/direction column.
    /// Returns the zero-based column index, or -1 if not found.
    /// </summary>
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
    // Uses StringBuilder — avoids O(n²) string concatenation with +=.
    // Handles RFC-4180 quoted fields that may contain commas.

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

        values.Add(current.ToString().Trim()); // last field (no trailing comma)
        return values.ToArray();
    }
}