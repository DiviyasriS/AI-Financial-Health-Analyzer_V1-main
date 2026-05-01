using OfficeOpenXml;
using System.Globalization;

// XlsxService parses Excel (.xlsx) files into Transaction objects
// It follows the exact same contract as CsvService — returns ParsedFileResult
// Both services are used by TransactionService through the same interface

public class XlsxService
{
    public XlsxService()
    {
        // EPPlus 6.x requires this license setting for non-commercial use
        // For commercial use, a paid license is needed
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
    }

    public async Task<ParsedFileResult> ParseAsync(
        Stream fileStream, int userId, ITransactionRepository repository)
    {
        var result = new ParsedFileResult();

        using var package = new ExcelPackage(fileStream);

        // Read the first worksheet
        var worksheet = package.Workbook.Worksheets.FirstOrDefault();

        if (worksheet == null || worksheet.Dimension == null)
            return result; // empty workbook

        var rowCount = worksheet.Dimension.Rows;
        var colCount = worksheet.Dimension.Columns;

        // Need at least a header row + one data row
        if (rowCount < 2)
            return result;

        // Row 1 is the header — start from row 2
        for (int row = 2; row <= rowCount; row++)
        {
            result.TotalRowsFound++;

            try
            {
                // Read each cell — GetValue returns null if cell is empty
                var dateRaw        = worksheet.Cells[row, 1].GetValue<string>();
                var descriptionRaw = worksheet.Cells[row, 2].GetValue<string>();
                var amountRaw      = worksheet.Cells[row, 3].GetValue<string>();
                var categoryRaw    = colCount >= 4
                                       ? worksheet.Cells[row, 4].GetValue<string>()
                                       : null;

                // Skip completely empty rows
                if (string.IsNullOrWhiteSpace(dateRaw) &&
                    string.IsNullOrWhiteSpace(descriptionRaw) &&
                    string.IsNullOrWhiteSpace(amountRaw))
                {
                    result.TotalRowsFound--; // don't count empty rows
                    continue;
                }

                // Validate required fields
                if (string.IsNullOrWhiteSpace(dateRaw) ||
                    string.IsNullOrWhiteSpace(descriptionRaw) ||
                    string.IsNullOrWhiteSpace(amountRaw))
                {
                    result.SkippedRows++;
                    continue;
                }

                var date        = DateTime.Parse(dateRaw.Trim());
                var description = descriptionRaw.Trim();
                var amount      = decimal.Parse(amountRaw.Trim(), CultureInfo.InvariantCulture);
                var category    = string.IsNullOrWhiteSpace(categoryRaw)
                                    ? "Uncategorized"
                                    : categoryRaw.Trim();

                if (amount == 0)
                {
                    result.SkippedRows++;
                    continue;
                }

                // Duplicate check
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
            catch
            {
                // Malformed row — skip it
                result.SkippedRows++;
            }
        }

        // EPPlus operations are sync internally — wrap in Task for consistency
        return await Task.FromResult(result);
    }
}