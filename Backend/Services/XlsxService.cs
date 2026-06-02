using OfficeOpenXml;
using System.Globalization;
using Backend.Models;
using Microsoft.Extensions.Logging;

// XlsxService parses a .xlsx/.xls workbook into Transaction objects.
//
// Expected column layout (header in row 1):
//   Col A: Date        (required)
//   Col B: Description (required)
//   Col C: Amount      (required; negative = debit, positive = credit)
//   Col D: Category    (optional; auto-predicted from description if blank)
//   Col E: Type        (optional; "CR"/"DR" explicit override for unsigned amounts)

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

        // ── Detect optional Type column (col E or header-named) ──────────────
        int typeColIndex = FindTypeColumnIndex(worksheet, colCount);

        for (int row = 2; row <= rowCount; row++)
        {
            ExcelRange dateCell        = worksheet.Cells[row, 1];
            ExcelRange descriptionCell = worksheet.Cells[row, 2];
            ExcelRange amountCell      = worksheet.Cells[row, 3];
            ExcelRange? categoryCell   = colCount >= 4 ? worksheet.Cells[row, 4] : null;
            ExcelRange? typeCell       = typeColIndex > 0 ? worksheet.Cells[row, typeColIndex] : null;

            if (dateCell.Value == null && descriptionCell.Value == null && amountCell.Value == null)
                continue;

            result.TotalRowsFound++;

            try
            {
                if (!TryParseDate(dateCell.Value, out DateTime date))
                {
                    _logger.LogDebug("XLSX row {Row} for user {UserId} has invalid date, skipping", row, userId);
                    result.SkippedRows++;
                    continue;
                }

                string? description = descriptionCell.Value?.ToString()?.Trim();
                if (string.IsNullOrWhiteSpace(description))
                {
                    result.SkippedRows++;
                    continue;
                }

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

                // ── Determine IsCredit ────────────────────────────────────────
                bool isCredit = DetermineIsCredit(rawAmount, typeCell?.Value?.ToString());

                decimal amount = Math.Abs(rawAmount);

                // ── Category resolution ───────────────────────────────────────
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

    // ── IsCredit determination ────────────────────────────────────────────────
    // Priority 1: explicit type column string
    // Priority 2: sign of the raw amount
    private static bool DetermineIsCredit(decimal rawAmount, string? typeValue)
    {
        if (!string.IsNullOrWhiteSpace(typeValue))
        {
            string t = typeValue.Trim().ToLowerInvariant();
            if (t is "credit" or "cr" or "in" or "received" or "true" or "1")
                return true;
            if (t is "debit" or "dr" or "out" or "paid" or "sent" or "false" or "0")
                return false;
        }

        return rawAmount > 0; // positive = credit, negative = debit
    }

    // ── Type column detection ─────────────────────────────────────────────────
    private static int FindTypeColumnIndex(ExcelWorksheet worksheet, int colCount)
    {
        if (colCount < 5) return -1;

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
    private static bool TryParseAmount(object? cellValue, out decimal amount)
    {
        amount = 0;
        if (cellValue == null) return false;

        if (cellValue is double d)   { amount = (decimal)d; return true; }
        if (cellValue is int i)      { amount = i;          return true; }
        if (cellValue is decimal dec){ amount = dec;        return true; }

        return decimal.TryParse(
            cellValue.ToString()?.Trim(),
            NumberStyles.Any,
            CultureInfo.InvariantCulture,
            out amount);
    }
}