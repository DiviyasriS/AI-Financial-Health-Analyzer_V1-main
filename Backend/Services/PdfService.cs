using System.Globalization;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

public class PdfService
{
    private readonly ILogger<PdfService> _logger;

    private static readonly Regex PaytmStatementRangeRegex = new(
        @"Paytm\s+Statement\s+for\s+(?<fromDay>\d{1,2})\s+(?<fromMonth>[A-Za-z]{3})'?\s*(?<fromYear>\d{2,4})\s*-\s*(?<toDay>\d{1,2})\s+(?<toMonth>[A-Za-z]{3})'?\s*(?<toYear>\d{2,4})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Matches date+time on the SAME line e.g. "28 May 6:02 PM" or "21 May 9:44 AM"
    private static readonly Regex PaytmDateLineRegex = new(
        @"^(?<day>\d{1,2})\s+(?<month>Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PaytmAmountRegex = new(
        @"(?<sign>[+-])\s*(?:Rs\.?|₹|INR)\s*(?<amount>\d{1,3}(?:,\d{3})*(?:\.\d{1,2})?|\d+(?:\.\d{1,2})?)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex AnyAmountRegex = new(
        @"(?:Rs\.?|₹|INR)\s*(?<amount>\d{1,3}(?:,\d{3})*(?:\.\d{1,2})?|\d+(?:\.\d{1,2})?)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Used to skip standalone time lines e.g. "10:05 AM" on its own row
    private static readonly Regex TimeLineRegex = new(
        @"^\d{1,2}:\d{2}\s*(AM|PM)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex BankLineRegex = new(
        @"^(City Union|Indian Bank)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex UpsertSpacesRegex = new(@"\s+", RegexOptions.Compiled);

    private static readonly Dictionary<string, string> CategoryAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Bill Payments"] = "Bills",
        ["Fuel"] = "Fuel",
        ["Food"] = "Food",
        ["Groceries"] = "Groceries",
        ["Shopping"] = "Shopping",
        ["Money Transfer"] = "Transfer",
        ["Money Received"] = "Income"
    };

    public PdfService(ILogger<PdfService> logger)
    {
        _logger = logger;
    }

    public Task<ParsedFileResult> ParseAsync(Stream fileStream, int userId)
    {
        var result = new ParsedFileResult();

        if (fileStream == null || !fileStream.CanRead)
        {
            _logger.LogWarning("PDF parse failed for user {UserId}: stream is null or unreadable.", userId);
            return Task.FromResult(result);
        }

        if (fileStream.CanSeek)
            fileStream.Position = 0;

        try
        {
            using PdfDocument document = PdfDocument.Open(fileStream);

            var lines = ExtractReadableLines(document);
            _logger.LogInformation("PDF text extraction for user {UserId}: extracted {LineCount} lines from {PageCount} pages.",
                userId, lines.Count, document.NumberOfPages);

            if (lines.Count == 0)
                return Task.FromResult(result);

            int defaultYear = DetectStatementEndYear(lines) ?? DateTime.Now.Year;
            var transactions = ParsePaytmLines(lines, userId, defaultYear);

            result.TotalRowsFound = transactions.Count;
            result.Transactions.AddRange(transactions);
            result.SkippedRows = 0;

            _logger.LogInformation("Paytm PDF parse complete for user {UserId}: Found={Found}, Parsed={Parsed}.",
                userId, result.TotalRowsFound, result.Transactions.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PDF parse failed for user {UserId}.", userId);
        }

        return Task.FromResult(result);
    }

    // ---------------------------------------------------------------------------
    // Text extraction
    // ---------------------------------------------------------------------------

    private static List<string> ExtractReadableLines(PdfDocument document)
    {
        var allLines = new List<string>();

        foreach (Page page in document.GetPages())
        {
            var wordLines = ExtractLinesFromWords(page);

            if (wordLines.Count > 0)
            {
                allLines.AddRange(wordLines);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(page.Text))
            {
                allLines.AddRange(page.Text
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(CleanLine)
                    .Where(l => !string.IsNullOrWhiteSpace(l)));
            }
        }

        return allLines;
    }

    private static List<string> ExtractLinesFromWords(Page page)
    {
        var words = page.GetWords().ToList();
        var lines = new List<string>();

        if (words.Count == 0)
            return lines;

        var grouped = words
            .GroupBy(w => Math.Round(w.BoundingBox.Bottom / 3.0) * 3.0)
            .OrderByDescending(g => g.Key);

        foreach (var group in grouped)
        {
            string line = string.Join(" ", group
                .OrderBy(w => w.BoundingBox.Left)
                .Select(w => w.Text));

            line = CleanLine(line);

            if (!string.IsNullOrWhiteSpace(line))
                lines.Add(line);
        }

        return lines;
    }

    // ---------------------------------------------------------------------------
    // Transaction parsing
    // ---------------------------------------------------------------------------

    private static List<Transaction> ParsePaytmLines(List<string> lines, int userId, int defaultYear)
    {
        var transactions = new List<Transaction>();

        for (int i = 0; i < lines.Count; i++)
        {
            string line = CleanLine(lines[i]);
            Match dateMatch = PaytmDateLineRegex.Match(line);

            if (!dateMatch.Success)
                continue;

            int blockStart = i;
            int blockEnd = FindNextPaytmTransactionStart(lines, i + 1);
            if (blockEnd < 0)
                blockEnd = lines.Count;

            var blockLines = lines
                .Skip(blockStart)
                .Take(blockEnd - blockStart)
                .Select(CleanLine)
                .ToList();

            Transaction? tx = TryParsePaytmBlock(blockLines, userId, defaultYear);

            if (tx != null)
                transactions.Add(tx);

            i = blockEnd - 1;
        }

        return transactions;
    }

    /// <summary>
    /// FIX 1 (previously): no longer requires a separate time-only line.
    /// A line matching PaytmDateLineRegex is sufficient to start a new block.
    /// </summary>
    private static int FindNextPaytmTransactionStart(List<string> lines, int startIndex)
    {
        for (int i = startIndex; i < lines.Count; i++)
        {
            if (PaytmDateLineRegex.IsMatch(CleanLine(lines[i])))
                return i;
        }

        return -1;
    }

    private static Transaction? TryParsePaytmBlock(List<string> blockLines, int userId, int defaultYear)
    {
        if (blockLines.Count < 3)
            return null;

        // FIX 2: Skip the statement summary header line.
        // e.g. "29 Apr 46 Payments made 4 Payments received"
        string fullBlock = CleanLine(string.Join(" ", blockLines));
        if (fullBlock.Contains("Payments made", StringComparison.OrdinalIgnoreCase) ||
            fullBlock.Contains("Payments received", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // Parse date from first line
        Match dateMatch = PaytmDateLineRegex.Match(blockLines[0]);
        if (!dateMatch.Success)
            return null;

        int day = int.Parse(dateMatch.Groups["day"].Value, CultureInfo.InvariantCulture);
        string monthText = dateMatch.Groups["month"].Value;
        int month = DateTime.ParseExact(monthText, "MMM", CultureInfo.InvariantCulture).Month;

        if (!TryGetYearForPaytmDate(day, month, defaultYear, out int year))
            return null;

        var date = new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc);

        // FIX 3: Detect credit/debit from the +/- sign in the amount.
        // Use only the first 8 lines of the block to avoid picking up amounts
        // from adjacent transaction blocks that leaked through.
        string localBlock = CleanLine(string.Join(" ", blockLines.Take(8)));
        var allSignedMatches = PaytmAmountRegex.Matches(localBlock).Cast<Match>().ToList();
        Match? amountMatch = allSignedMatches.LastOrDefault();
        bool isCredit = false;

        if (amountMatch != null && amountMatch.Success)
        {
            isCredit = amountMatch.Groups["sign"].Value == "+";
        }
        else
        {
            // Fallback: unsigned amount pattern, scoped to local block only
            amountMatch = AnyAmountRegex.Matches(localBlock).Cast<Match>().LastOrDefault();
            if (amountMatch == null || !amountMatch.Success)
                return null;
        }

        string rawAmount = amountMatch.Groups["amount"].Value.Replace(",", "");
        if (!decimal.TryParse(rawAmount, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal amount))
            return null;

        string category = ExtractPaytmCategory(fullBlock);
        string description = ExtractPaytmDescription(blockLines);

        if (string.IsNullOrWhiteSpace(description))
            return null;

        // Override category for received money if the tag didn't already map it
        if (isCredit && string.Equals(category, "Uncategorized", StringComparison.OrdinalIgnoreCase))
            category = "Income";

        return new Transaction
        {
            Date = date,
            Description = description,
            Amount = Math.Abs(amount),
            IsCredit = isCredit,
            Category = category,
            UserId = userId
        };
    }

    /// <summary>
    /// FIX 1 (part 2): description extraction now starts at index 1, not 2,
    /// because the date+time are on the SAME line (blockLines[0]), so
    /// blockLines[1] is the first real description line.
    /// FIX 4: also stops on bank-name lines ("City Union Bank", "Indian Bank")
    /// that leak in from the account column.
    /// </summary>
    private static string ExtractPaytmDescription(List<string> blockLines)
    {
        var descriptionLines = new List<string>();

        for (int i = 1; i < blockLines.Count; i++)
        {
            string line = CleanLine(blockLines[i]);

            // Skip standalone time lines e.g. "10:05 AM" that appear on their own row
            if (TimeLineRegex.IsMatch(line)) continue;

            if (line.StartsWith("UPI ID", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("UPI Ref No", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Order ID", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Tag:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("#") ||
                line.StartsWith("Note:", StringComparison.OrdinalIgnoreCase) ||
                PaytmAmountRegex.IsMatch(line) ||
                line.Contains("Bank -", StringComparison.OrdinalIgnoreCase) ||
                BankLineRegex.IsMatch(line))
            {
                break;
            }

            descriptionLines.Add(line);
        }

        string description = CleanLine(string.Join(" ", descriptionLines));

        // Strip trailing "Tag: / City Union / Indian Bank / HH:MM AM/PM" artifacts
        // that sometimes bleed in from the account column on the same line
        description = Regex.Replace(description,
            @"\s+(Tag:|City Union|Indian Bank|\d{1,2}:\d{2}\s*(AM|PM)).*$",
            "", RegexOptions.IgnoreCase).Trim();

        return string.IsNullOrWhiteSpace(description)
            ? "Paytm Transaction"
            : description;
    }

    private static string ExtractPaytmCategory(string fullBlock)
    {
        Match tagMatch = Regex.Match(fullBlock, @"#\s*(?<tag>[A-Za-z ]+)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        if (!tagMatch.Success)
            return "Uncategorized";

        string rawTag = CleanLine(tagMatch.Groups["tag"].Value);
        rawTag = Regex.Replace(rawTag,
            @"\b(City|Union|Indian|Bank|Rs|UPI|Ref|No|Order|ID)\b.*$",
            "", RegexOptions.IgnoreCase).Trim();

        return CategoryAliases.TryGetValue(rawTag, out string? mapped)
            ? mapped
            : rawTag;
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static int? DetectStatementEndYear(List<string> lines)
    {
        string allText = string.Join(" ", lines);
        Match match = PaytmStatementRangeRegex.Match(allText);

        if (!match.Success)
        {
            Match anyYear = Regex.Match(allText, @"\b20\d{2}\b");
            return anyYear.Success
                ? int.Parse(anyYear.Value, CultureInfo.InvariantCulture)
                : null;
        }

        string rawYear = match.Groups["toYear"].Value;
        int year = int.Parse(rawYear, CultureInfo.InvariantCulture);
        return year < 100 ? 2000 + year : year;
    }

    private static bool TryGetYearForPaytmDate(int day, int month, int defaultYear, out int year)
    {
        year = defaultYear;
        try
        {
            _ = new DateTime(year, month, day);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string CleanLine(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        value = value.Replace("#️", "#").Replace("₹", "Rs.");
        return UpsertSpacesRegex.Replace(value, " ").Trim();
    }
}