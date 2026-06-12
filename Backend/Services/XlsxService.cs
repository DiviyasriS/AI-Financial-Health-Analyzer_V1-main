using OfficeOpenXml;
using System.Globalization;
using Backend.Models;
using Microsoft.Extensions.Logging;

// XlsxService parses a .xlsx/.xls workbook into Transaction objects.
//
// Expected column layout (header in row 1):
//   Col A: Date        (required)
//   Col B: Description (required)
//   Col C: Amount      (required)
//   Col D: Category    (optional; auto-predicted from description if blank)
//   Col E: Type        (optional; "CR"/"DR" explicit override for unsigned amounts)
//
// DIRECTION DETECTION — three-tier priority (mirrors CsvService):
//
//   TIER 1 — Explicit type column: "Credit", "CR", "Debit", "DR", etc.
//             Always takes precedence when present.
//
//   TIER 2 — Signed amounts: if ANY amount in the worksheet is negative, the
//             export uses sign convention (negative = debit, positive = credit).
//
//   TIER 3 — Unsigned export (all amounts positive, no type column): default
//             to IsCredit = false (debit) for every row.
//             This is correct for most Indian bank XLSX exports (HDFC, ICICI,
//             SBI, Axis) which export all amounts as positive numbers.
//
// IMPORTANT: the previous implementation used `return rawAmount > 0` as the
// fallback, which always returns true for unsigned exports, misclassifying
// every debit as income. The fix applies the same signed-export detection
// approach used in CsvService (Improvement #1).

public class XlsxService
{
    private readonly ILogger<XlsxService> _logger;
    private readonly CategoryPredictionService _categoryPredictor;

    public XlsxService(ILogger<XlsxService> logger, CategoryPredictionService categoryPredictor)
    {
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        _logger            = logger;
        _categoryPredictor = categoryPredictor;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public Task<ParsedFileResult> ParseAsync(Stream fileStream, int userId)
    {
        var result = new ParsedFileResult();

        using ExcelPackage package = new ExcelPackage(fileStream);
        ExcelWorksheet? worksheet = package.Workbook.Worksheets.FirstOrDefault();

        if (worksheet?.Dimension == null)
        {
            _logger.LogWarning("XLSX file for user {UserId} has no worksheet or is empty", userId);
            return Task.FromResult(result);
        }

        int rowCount = worksheet.Dimension.Rows;
        int colCount = worksheet.Dimension.Columns;

        if (rowCount < 2)
        {
            _logger.LogWarning("XLSX file for user {UserId} has no data rows", userId);
            return Task.FromResult(result);
        }

        // ── Detect optional Type column (header-named) ────────────────────────
        int typeColIndex = FindTypeColumnIndex(worksheet, colCount);

        // ── Detect signed vs unsigned export ─────────────────────────────────
        // Scan up to 50 data rows before parsing to decide the direction strategy.
        bool isSignedExport = DetectSignedExport(worksheet, rowCount);

        _logger.LogInformation(
            "XLSX format detection for user {UserId}: TypeColIndex={TypeCol}, IsSignedExport={Signed}, DataRows={Rows}",
            userId, typeColIndex, isSignedExport, rowCount - 1);

        // ── Parse each data row ───────────────────────────────────────────────
        for (int row = 2; row <= rowCount; row++)
        {
            ExcelRange dateCell        = worksheet.Cells[row, 1];
            ExcelRange descriptionCell = worksheet.Cells[row, 2];
            ExcelRange amountCell      = worksheet.Cells[row, 3];
            ExcelRange? categoryCell   = colCount >= 4 ? worksheet.Cells[row, 4] : null;
            ExcelRange? typeCell       = typeColIndex > 0 ? worksheet.Cells[row, typeColIndex] : null;

            // Skip completely blank rows
            if (dateCell.Value == null && descriptionCell.Value == null && amountCell.Value == null)
                continue;

            result.TotalRowsFound++;

            try
            {
                // ── Date ──────────────────────────────────────────────────────
                if (!TryParseDate(dateCell.Value, out DateTime date))
                {
                    _logger.LogDebug("XLSX row {Row} for user {UserId} has invalid date, skipping", row, userId);
                    result.SkippedRows++;
                    continue;
                }

                // ── Description ───────────────────────────────────────────────
                string? description = descriptionCell.Value?.ToString()?.Trim();
                if (string.IsNullOrWhiteSpace(description))
                {
                    result.SkippedRows++;
                    continue;
                }

                // ── Amount ────────────────────────────────────────────────────
                if (!TryParseAmount(amountCell.Value, out decimal rawAmount))
                {
                    _logger.LogDebug("XLSX row {Row} for user {UserId} has invalid amount, skipping", row, userId);
                    result.SkippedRows++;
                    continue;
                }

                if (rawAmount == 0)
                {
                    result.SkippedRows++;
                    continue;
                }

                // ── Direction (IsCredit) ───────────────────────────────────────
                bool isCredit = DetermineIsCredit(
                    rawAmount,
                    typeCell?.Value?.ToString(),
                    isSignedExport);

                decimal amount = Math.Abs(rawAmount);

                // ── Category ──────────────────────────────────────────────────
                string? category = categoryCell?.Value?.ToString()?.Trim();

                if (string.IsNullOrWhiteSpace(category) ||
                    string.Equals(category, "Uncategorized", StringComparison.OrdinalIgnoreCase))
                {
                    category = _categoryPredictor.Predict(description);
                }

                // Credits with no specific category → Income
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
                _logger.LogWarning(ex, "XLSX row {Row} for user {UserId} failed to parse, skipping", row, userId);
                result.SkippedRows++;
            }
        }

        _logger.LogInformation(
            "XLSX parse complete for user {UserId}: {Total} rows, {Valid} valid ({Credits} credits, {Debits} debits), {Skipped} skipped",
            userId,
            result.TotalRowsFound,
            result.Transactions.Count,
            result.Transactions.Count(t => t.IsCredit),
            result.Transactions.Count(t => !t.IsCredit),
            result.SkippedRows);

        return Task.FromResult(result);
    }

    // ── Direction detection ───────────────────────────────────────────────────

    /// <summary>
    /// Determines whether a row represents money received (IsCredit = true)
    /// or money spent (IsCredit = false).
    ///
    /// Decision hierarchy:
    ///
    ///   1. Explicit type column — always wins.
    ///      Values recognised as credit: "credit", "cr", "in", "received", "true", "1"
    ///      Values recognised as debit:  "debit",  "dr", "out", "paid", "sent", "false", "0"
    ///      Unknown type string → fall through.
    ///
    ///   2. Signed export (isSignedExport == true):
    ///      rawAmount &lt; 0 → Debit  (outgoing money)
    ///      rawAmount &gt; 0 → Credit (incoming money)
    ///
    ///   3. Unsigned export (isSignedExport == false, no type column):
    ///      All amounts are positive; no direction information available.
    ///      → Default to Debit (IsCredit = false).
    ///      This is the correct conservative assumption for Indian bank XLSX exports.
    /// </summary>
    private static bool DetermineIsCredit(decimal rawAmount, string? typeValue, bool isSignedExport)
    {
        // Tier 1: explicit type column
        if (!string.IsNullOrWhiteSpace(typeValue))
        {
            string t = typeValue.Trim().ToLowerInvariant();
            if (t is "credit" or "cr" or "in" or "received" or "true" or "1")
                return true;
            if (t is "debit" or "dr" or "out" or "paid" or "sent" or "false" or "0")
                return false;
            // Unknown type string — fall through to sign-based logic
        }

        // Tier 2: signed export — use sign of the amount
        if (isSignedExport)
        {
            return rawAmount > 0; // negative = debit, positive = credit
        }

        // Tier 3: unsigned export with no type column → conservatively treat as debit
        return false;
    }

    // ── Signed-export detector ────────────────────────────────────────────────

    /// <summary>
    /// Scans up to 50 data rows (rows 2–51) to determine whether the workbook
    /// uses signed amounts. A single negative value is sufficient to confirm it.
    /// </summary>
    private static bool DetectSignedExport(ExcelWorksheet worksheet, int rowCount)
    {
        int maxRow = Math.Min(rowCount, 51); // rows 2..51

        for (int row = 2; row <= maxRow; row++)
        {
            object? cellValue = worksheet.Cells[row, 3].Value;
            if (TryParseAmount(cellValue, out decimal amount) && amount < 0)
                return true; // confirmed signed export
        }

        return false; // no negative amounts found → unsigned export
    }

    // ── Type column detection ─────────────────────────────────────────────────

    /// <summary>
    /// Searches header row (row 1) for a type/direction column.
    /// Returns the 1-based column index, or -1 if not found.
    /// </summary>
    private static int FindTypeColumnIndex(ExcelWorksheet worksheet, int colCount)
    {
        for (int col = 1; col <= colCount; col++)
        {
            string? header = worksheet.Cells[1, col].Value?.ToString()?.Trim().ToLowerInvariant();
            if (header is "type" or "iscredit" or "credit/debit" or "dr/cr" or "cr/dr"
                       or "transaction type" or "txn type" or "nature")
                return col;
        }

        return -1;
    }

    // ── Date parsing ──────────────────────────────────────────────────────────

    /// <summary>
    /// Handles EPPlus OA date doubles, CLR DateTime cells, and string date values.
    /// </summary>
    private static bool TryParseDate(object? cellValue, out DateTime date)
    {
        date = default;

        if (cellValue is null)
            return false;

        if (cellValue is double oaDate)
        {
            date = DateTime.SpecifyKind(DateTime.FromOADate(oaDate).Date, DateTimeKind.Utc);
            return true;
        }

        if (cellValue is DateTime dt)
        {
            date = DateTime.SpecifyKind(dt.Date, DateTimeKind.Utc);
            return true;
        }

        if (DateTime.TryParse(
            cellValue.ToString()?.Trim(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal,
            out DateTime parsed))
        {
            date = DateTime.SpecifyKind(parsed.Date, DateTimeKind.Utc);
            return true;
        }

        return false;
    }

    // ── Amount parsing ────────────────────────────────────────────────────────

    /// <summary>
    /// Handles numeric cell types (double, int, decimal) as well as string
    /// representations, including those with currency symbols and commas.
    /// </summary>
    private static bool TryParseAmount(object? cellValue, out decimal amount)
    {
        amount = 0;
        if (cellValue == null) return false;

        if (cellValue is double d)   { amount = (decimal)d; return true; }
        if (cellValue is int i)      { amount = i;          return true; }
        if (cellValue is decimal dec){ amount = dec;        return true; }

        // String fallback: strip currency symbols and thousands separators
        string raw = (cellValue.ToString() ?? string.Empty)
            .Replace("₹", "").Replace("$", "").Replace("€", "").Replace("£", "")
            .Replace(",", "")
            .Trim();

        return decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out amount);
    }
}