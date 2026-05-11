using OfficeOpenXml;
using System.Globalization;

public class XlsxService
{
    private readonly ILogger<XlsxService> _logger;

    public XlsxService(ILogger<XlsxService> logger)
    {
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        _logger = logger;
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

        for (int row = 2; row <= rowCount; row++)
        {
            ExcelRange dateCell        = worksheet.Cells[row, 1];
            ExcelRange descriptionCell = worksheet.Cells[row, 2];
            ExcelRange amountCell      = worksheet.Cells[row, 3];
            ExcelRange? categoryCell   = colCount >= 4 ? worksheet.Cells[row, 4] : null;

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

                if (!TryParseAmount(amountCell.Value, out decimal amount))
                {
                    _logger.LogDebug("XLSX row {Row} for user {UserId} has invalid amount, skipping", row, userId);
                    result.SkippedRows++;
                    continue;
                }

                if (amount == 0)
                {
                    result.SkippedRows++;
                    continue;
                }

                string? category = categoryCell?.Value?.ToString()?.Trim();
                if (string.IsNullOrWhiteSpace(category))
                    category = "Uncategorized";

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
                _logger.LogWarning(ex, "XLSX row {Row} for user {UserId} failed to parse, skipping", row, userId);
                result.SkippedRows++;
            }
        }

        _logger.LogInformation("XLSX parse complete for user {UserId}: {Total} rows, {Valid} valid, {Skipped} skipped",
            userId, result.TotalRowsFound, result.Transactions.Count, result.SkippedRows);

        return Task.FromResult(result);
    }

    private static bool TryParseDate(object? cellValue, out DateTime date)
{
    date = default;

    if (cellValue is null)
        return false;

    // Excel stores dates internally as OLE Automation numbers
    if (cellValue is double oaDate)
    {
        date = DateTime.SpecifyKind(
            DateTime.FromOADate(oaDate).Date,
            DateTimeKind.Utc);

        return true;
    }

    // Already parsed as DateTime by EPPlus
    if (cellValue is DateTime dt)
    {
        date = DateTime.SpecifyKind(
            dt.Date,
            DateTimeKind.Utc);

        return true;
    }

    // String parsing fallback
    if (DateTime.TryParse(
        cellValue.ToString()?.Trim(),
        CultureInfo.InvariantCulture,
        DateTimeStyles.AssumeLocal,
        out DateTime parsed))
    {
        date = DateTime.SpecifyKind(
            parsed.Date,
            DateTimeKind.Utc);

        return true;
    }

    return false;
}
    private static bool TryParseAmount(object? cellValue, out decimal amount)
    {
        amount = 0;
        if (cellValue == null) return false;

        if (cellValue is double d) { amount = (decimal)d; return true; }
        if (cellValue is int i)    { amount = i; return true; }
        if (cellValue is decimal dec) { amount = dec; return true; }

        return decimal.TryParse(cellValue.ToString()?.Trim(), NumberStyles.Any,
            CultureInfo.InvariantCulture, out amount);
    }
}