using System.Globalization;

// CsvService is responsible for ONE thing: parsing a CSV stream into Transaction objects
// It does NOT save to the database — that is TransactionService's job
// It uses ITransactionRepository only to check for duplicates during parsing

public class CsvService
{
    // ─── PARSE CSV ───────────────────────────────────────────────────────────

    public async Task<List<Transaction>> ParseCsvAsync(
        Stream fileStream, int userId, ITransactionRepository repository)
    {
        var transactions = new List<Transaction>();
        var skippedRows = 0;

        using var reader = new StreamReader(fileStream);

        // Skip the header row
        var header = await reader.ReadLineAsync();

        if (header == null)
            return transactions; // empty file

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync();

            if (string.IsNullOrWhiteSpace(line))
                continue;

            var values = line.Split(',');

            // Need at least Date, Description, Amount
            if (values.Length < 3)
            {
                skippedRows++;
                continue;
            }

            try
            {
                var date        = DateTime.Parse(values[0].Trim());
                var description = values[1].Trim();
                var amount      = decimal.Parse(values[2].Trim(), CultureInfo.InvariantCulture);
                var category    = values.Length > 3
                                    ? values[3].Trim()
                                    : "Uncategorized";

                // Skip rows with empty description
                if (string.IsNullOrWhiteSpace(description))
                {
                    skippedRows++;
                    continue;
                }

                // Skip empty or zero-amount rows
                if (amount == 0)
                {
                    skippedRows++;
                    continue;
                }

                // Duplicate check — asks the repository if this exact record exists
                var isDuplicate = await repository.DuplicateExistsAsync(
                    userId, date, description, amount);

                if (isDuplicate)
                    continue;

                transactions.Add(new Transaction
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
            catch
            {
                // Skip malformed rows silently — don't crash the entire upload
                skippedRows++;
                continue;
            }
        }

        return transactions;
    }
}