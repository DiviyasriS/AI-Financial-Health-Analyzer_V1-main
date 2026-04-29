using System.Globalization;

public class CsvService
{
    public List<Transaction> ProcessCsv(Stream fileStream, int userId, AppDbContext context)
{
    var transactions = new List<Transaction>();

    using (var reader = new StreamReader(fileStream))
    {
        var header = reader.ReadLine();

        while (!reader.EndOfStream)
        {
            var line = reader.ReadLine();
            var values = line.Split(',');

            try
            {
                var date = DateTime.Parse(values[0]);
                var description = values[1]?.Trim();
                var amount = decimal.Parse(values[2], CultureInfo.InvariantCulture);
                var category = values.Length > 3 ? values[3] : "Uncategorized";

                // 🚨 Duplicate Check
                bool exists = context.Transactions.Any(t =>
                    t.Date == date &&
                    t.Description == description &&
                    t.Amount == amount &&
                    t.UserId == userId
                );

                if (exists) continue;

                var transaction = new Transaction
                {
                    Date = date,
                    Description = description,
                    Amount = amount,
                    Category = category,
                    UserId = userId
                };

                if (string.IsNullOrEmpty(description))
                    continue;

                transactions.Add(transaction);
            }
            catch
            {
                continue;
            }
        }
    }

    return transactions;
}
}