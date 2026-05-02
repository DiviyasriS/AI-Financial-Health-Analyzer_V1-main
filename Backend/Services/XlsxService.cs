using OfficeOpenXml;
using System.Globalization;

public class XlsxService
{
    public XlsxService()
    {
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
    }

    public async Task<ParsedFileResult> ParseAsync(
        Stream fileStream, int userId, ITransactionRepository repository)
    {
        var result = new ParsedFileResult();

        using var package = new ExcelPackage(fileStream);

        var worksheet = package.Workbook.Worksheets.FirstOrDefault();

        if (worksheet == null || worksheet.Dimension == null)
            return result;

        var rowCount = worksheet.Dimension.Rows;
        var colCount = worksheet.Dimension.Columns;

        if (rowCount < 2)
            return result;

        for (int row = 2; row <= rowCount; row++)
        {
            // Read raw cell objects — do NOT use GetValue<string>() for dates
            var dateCell        = worksheet.Cells[row, 1];
            var descriptionCell = worksheet.Cells[row, 2];
            var amountCell      = worksheet.Cells[row, 3];
            var categoryCell    = colCount >= 4 ? worksheet.Cells[row, 4] : null;

            // Skip completely empty rows
            if (dateCell.Value == null &&
                descriptionCell.Value == null &&
                amountCell.Value == null)
                continue;

            result.TotalRowsFound++;

            try
            {
                // ── Parse Date ────────────────────────────────────────────────
                // Excel stores dates as OLE Automation doubles (e.g. 45353.0)
                // We must handle both: numeric OA date AND string date
                DateTime date;

                if (dateCell.Value == null)
                {
                    result.SkippedRows++;
                    continue;
                }
                else if (dateCell.Value is double oaDate)
                {
                    // Excel numeric date — convert using OLE Automation
                    date = DateTime.FromOADate(oaDate);
                }
                else if (dateCell.Value is DateTime directDate)
                {
                    // EPPlus already parsed it as DateTime
                    date = directDate;
                }
                else
                {
                    // Stored as a string — try parsing directly
                    var dateStr = dateCell.Value.ToString()?.Trim();
                    if (!DateTime.TryParse(dateStr, out date))
                    {
                        result.SkippedRows++;
                        continue;
                    }
                }

                // ── Parse Description ─────────────────────────────────────────
                var description = descriptionCell.Value?.ToString()?.Trim();

                if (string.IsNullOrWhiteSpace(description))
                {
                    result.SkippedRows++;
                    continue;
                }

                // ── Parse Amount ──────────────────────────────────────────────
                // Amount cell can be a double, int, or string
                decimal amount;

                if (amountCell.Value == null)
                {
                    result.SkippedRows++;
                    continue;
                }
                else if (amountCell.Value is double dblAmount)
                {
                    amount = (decimal)dblAmount;
                }
                else if (amountCell.Value is int intAmount)
                {
                    amount = (decimal)intAmount;
                }
                else
                {
                    var amountStr = amountCell.Value.ToString()?.Trim();
                    if (!decimal.TryParse(amountStr, NumberStyles.Any,
                            CultureInfo.InvariantCulture, out amount))
                    {
                        result.SkippedRows++;
                        continue;
                    }
                }

                if (amount == 0)
                {
                    result.SkippedRows++;
                    continue;
                }

                // ── Parse Category ────────────────────────────────────────────
                var category = categoryCell?.Value?.ToString()?.Trim();
                if (string.IsNullOrWhiteSpace(category))
                    category = "Uncategorized";

                // ── Duplicate check ───────────────────────────────────────────
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
                    Category    = category,
                    UserId      = userId
                });
            }
            catch (Exception ex)
            {
                // Log which row failed to help with debugging
                Console.WriteLine($"[XlsxService] Skipping row {row}: {ex.Message}");
                result.SkippedRows++;
            }
        }

        return await Task.FromResult(result);
    }
}