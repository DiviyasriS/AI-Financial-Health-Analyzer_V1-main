using System.Globalization;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

public class PdfService
{
    private readonly ILogger<PdfService> _logger;

    private static readonly string[] DateFormats =
    {
        "dd/MM/yyyy", "d/M/yyyy",
        "MM/dd/yyyy", "M/d/yyyy",
        "yyyy-MM-dd", "yyyy/MM/dd",
        "dd-MM-yyyy", "d-M-yyyy",

        "dd MMM yyyy", "d MMM yyyy",
        "dd MMM, yyyy", "d MMM, yyyy",
        "dd-MMM-yyyy", "d-MMM-yyyy",
        "MMM dd yyyy", "MMM d yyyy",
        "MMM dd, yyyy", "MMM d, yyyy",

        "dd/MM/yy", "d/M/yy",
        "MM/dd/yy", "M/d/yy",

        "dd/MM/yyyy HH:mm:ss", "d/M/yyyy HH:mm:ss",
        "dd/MM/yyyy HH:mm", "d/M/yyyy HH:mm",
        "dd/MM/yyyy h:mm:ss tt", "d/M/yyyy h:mm:ss tt",
        "dd/MM/yyyy h:mm tt", "d/M/yyyy h:mm tt",

        "dd-MM-yyyy HH:mm:ss", "d-M-yyyy HH:mm:ss",
        "dd-MM-yyyy HH:mm", "d-M-yyyy HH:mm",
        "dd-MM-yyyy h:mm:ss tt", "d-M-yyyy h:mm:ss tt",
        "dd-MM-yyyy h:mm tt", "d-M-yyyy h:mm tt",

        "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm",
        "yyyy-MM-dd h:mm:ss tt", "yyyy-MM-dd h:mm tt",

        "dd MMM yyyy HH:mm:ss", "d MMM yyyy HH:mm:ss",
        "dd MMM yyyy HH:mm", "d MMM yyyy HH:mm",
        "dd MMM yyyy h:mm:ss tt", "d MMM yyyy h:mm:ss tt",
        "dd MMM yyyy h:mm tt", "d MMM yyyy h:mm tt",

        "dd MMM, yyyy HH:mm:ss", "d MMM, yyyy HH:mm:ss",
        "dd MMM, yyyy HH:mm", "d MMM, yyyy HH:mm",
        "dd MMM, yyyy h:mm:ss tt", "d MMM, yyyy h:mm:ss tt",
        "dd MMM, yyyy h:mm tt", "d MMM, yyyy h:mm tt",

        "MMM dd yyyy HH:mm:ss", "MMM d yyyy HH:mm:ss",
        "MMM dd yyyy HH:mm", "MMM d yyyy HH:mm",
        "MMM dd yyyy h:mm:ss tt", "MMM d yyyy h:mm:ss tt",
        "MMM dd yyyy h:mm tt", "MMM d yyyy h:mm tt",

        "MMM dd, yyyy HH:mm:ss", "MMM d, yyyy HH:mm:ss",
        "MMM dd, yyyy HH:mm", "MMM d, yyyy HH:mm",
        "MMM dd, yyyy h:mm:ss tt", "MMM d, yyyy h:mm:ss tt",
        "MMM dd, yyyy h:mm tt", "MMM d, yyyy h:mm tt"
    };

    private static readonly Regex LeadingDateTimeRegex = new(
        @"^\s*(?<date>(?:\d{1,2}[/-]\d{1,2}[/-]\d{2,4}|\d{4}[/-]\d{1,2}[/-]\d{1,2}|\d{1,2}\s+[A-Za-z]{3,9},?\s+\d{2,4}|[A-Za-z]{3,9}\s+\d{1,2},?\s+\d{2,4})(?:[,\s]+\d{1,2}:\d{2}(?::\d{2})?\s*(?:AM|PM|am|pm)?)?)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex AmountRegex = new(
        @"^[(\-+]?\s*(?:₹|rs\.?|inr)?\s*[\d,\.]+\s*(?:cr|dr)?\s*\)?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> SkipKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "date", "description", "narration", "particulars",
        "amount", "debit", "credit", "balance", "closing",
        "opening", "total", "sub-total", "subtotal",
        "carried forward", "brought forward", "page", "statement"
    };

    public PdfService(ILogger<PdfService> logger)
    {
        _logger = logger;
    }

    public Task<ParsedFileResult> ParseAsync(Stream fileStream, int userId)
    {
        ParsedFileResult result = new ParsedFileResult();

        if (fileStream == null || fileStream.Length == 0)
        {
            _logger.LogWarning("PDF upload for user {UserId}: empty stream.", userId);
            return Task.FromResult(result);
        }

        PdfDocument? document = null;

        try
        {
            document = PdfDocument.Open(fileStream);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PDF upload for user {UserId}: cannot open PDF.", userId);
            return Task.FromResult(result);
        }

        using (document)
        {
            if (document.NumberOfPages == 0)
            {
                _logger.LogWarning("PDF upload for user {UserId}: document has no pages.", userId);
                return Task.FromResult(result);
            }

            List<string> allLines = ExtractLinesFromDocument(document, userId);
            List<string> transactionBlocks = MergeGooglePayTransactionLines(allLines);

            foreach (string rawLine in transactionBlocks)
            {
                string line = rawLine.Trim();

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (IsHeaderOrFooterRow(line))
                {
                    continue;
                }

                result.TotalRowsFound++;

                Transaction? transaction = TryParseLine(line, userId);

                if (transaction != null)
                {
                    result.Transactions.Add(transaction);
                }
                else
                {
                    result.SkippedRows++;
                    _logger.LogDebug("PDF user {UserId}: skipped row: {Line}", userId, line);
                }
            }
        }

        _logger.LogInformation(
            "PDF parse complete for user {UserId}: {Total} rows found, {Valid} valid, {Skipped} skipped.",
            userId,
            result.TotalRowsFound,
            result.Transactions.Count,
            result.SkippedRows);

        return Task.FromResult(result);
    }

    private List<string> ExtractLinesFromDocument(PdfDocument document, int userId)
    {
        List<string> allLines = new List<string>();

        for (int pageNum = 1; pageNum <= document.NumberOfPages; pageNum++)
        {
            try
            {
                Page page = document.GetPage(pageNum);
                IReadOnlyList<Word> words = page.GetWords().ToList();

                if (words.Count == 0)
                {
                    continue;
                }

                IEnumerable<IGrouping<int, Word>> lineGroups = words
                    .GroupBy(word => (int)Math.Round(word.BoundingBox.Top / 3.0) * 3)
                    .OrderByDescending(group => group.Key);

                foreach (IGrouping<int, Word> group in lineGroups)
                {
                    string line = string.Join(" ",
                        group
                            .OrderBy(word => word.BoundingBox.Left)
                            .Select(word => word.Text));

                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        allLines.Add(line);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "PDF user {UserId}: failed to read page {Page}.", userId, pageNum);
            }
        }

        return allLines;
    }

    private static List<string> MergeGooglePayTransactionLines(List<string> lines)
    {
        List<string> mergedLines = new List<string>();
        string currentBlock = string.Empty;

        foreach (string rawLine in lines)
        {
            string line = rawLine.Trim();

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            bool startsWithDate = LeadingDateTimeRegex.IsMatch(line);

            if (startsWithDate)
            {
                if (!string.IsNullOrWhiteSpace(currentBlock))
                {
                    mergedLines.Add(currentBlock.Trim());
                }

                currentBlock = line;
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(currentBlock))
                {
                    currentBlock = $"{currentBlock} {line}";
                }
                else
                {
                    mergedLines.Add(line);
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(currentBlock))
        {
            mergedLines.Add(currentBlock.Trim());
        }

        return mergedLines;
    }

    private static bool IsHeaderOrFooterRow(string line)
    {
        string lower = line.ToLowerInvariant();

        if (SkipKeywords.Any(keyword => lower.Contains(keyword)) &&
            !LeadingDateTimeRegex.IsMatch(line))
        {
            return true;
        }

        if (line.Replace(" ", "").Length < 5)
        {
            return true;
        }

        if (Regex.IsMatch(line.Trim(), @"^\d{1,3}$"))
        {
            return true;
        }

        return false;
    }

    private Transaction? TryParseLine(string line, int userId)
    {
        if (TryExtractLeadingDateTime(line, out DateTime leadingDate, out string remainingText))
        {
            Transaction? parsed = TryBuildFromTextAfterDate(remainingText, leadingDate, userId);

            if (parsed != null)
            {
                return parsed;
            }
        }

        string[] tokens = Regex.Split(line, @"\s{2,}|\t")
            .Select(token => token.Trim())
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .ToArray();

        if (tokens.Length < 2)
        {
            return null;
        }

        if (TryParseDate(tokens[0], out DateTime date))
        {
            return TryBuildFromDateFirst(tokens, date, userId);
        }

        if (tokens.Length >= 3 && TryParseDate($"{tokens[0]} {tokens[1]}", out date))
        {
            string[] shifted = new[] { $"{tokens[0]} {tokens[1]}" }
                .Concat(tokens.Skip(2))
                .ToArray();

            return TryBuildFromDateFirst(shifted, date, userId);
        }

        string[] singleSplit = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (singleSplit.Length >= 3 && TryParseDate(singleSplit[0], out date))
        {
            return TryBuildFromLooseLine(singleSplit, date, userId);
        }

        return null;
    }

    private Transaction? TryBuildFromTextAfterDate(string textAfterDate, DateTime date, int userId)
    {
        if (string.IsNullOrWhiteSpace(textAfterDate))
        {
            return null;
        }

        string[] looseTokens = textAfterDate.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (looseTokens.Length < 2)
        {
            return null;
        }

        List<(string Token, int Index)> amountCandidates = looseTokens
            .Select((token, index) => (Token: token.Trim(), Index: index))
            .Where(item => AmountRegex.IsMatch(item.Token.Replace(" ", "")))
            .ToList();

        for (int i = 0; i < looseTokens.Length - 1; i++)
        {
            string combined = $"{looseTokens[i]}{looseTokens[i + 1]}";

            if (AmountRegex.IsMatch(combined.Replace(" ", "")))
            {
                amountCandidates.Add((combined, i));
            }
        }

        if (amountCandidates.Count == 0)
        {
            return null;
        }

        (string Token, int Index) amountCandidate = amountCandidates
            .OrderByDescending(candidate => candidate.Index)
            .First();

        if (!TryParseAmount(amountCandidate.Token, out decimal amount))
        {
            return null;
        }

        if (amount == 0m)
        {
            return null;
        }

        string description = string.Join(" ", looseTokens.Take(amountCandidate.Index)).Trim();

        if (string.IsNullOrWhiteSpace(description))
        {
            return null;
        }

        return new Transaction
        {
            Date = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc),
            Description = description,
            Amount = Math.Abs(amount),
            Category = "Uncategorized",
            UserId = userId
        };
    }

    private Transaction? TryBuildFromDateFirst(string[] tokens, DateTime date, int userId)
    {
        if (tokens.Length < 2)
        {
            return null;
        }

        int amountIndex = -1;

        for (int i = tokens.Length - 1; i >= 1; i--)
        {
            if (AmountRegex.IsMatch(tokens[i].Replace(" ", "")))
            {
                amountIndex = i;
                break;
            }
        }

        if (amountIndex < 0)
        {
            return null;
        }

        if (!TryParseAmount(tokens[amountIndex], out decimal amount))
        {
            return null;
        }

        if (amount == 0m)
        {
            return null;
        }

        string description = string.Join(" ", tokens.Skip(1).Take(amountIndex - 1)).Trim();

        if (string.IsNullOrWhiteSpace(description))
        {
            return null;
        }

        string category = amountIndex + 1 < tokens.Length
            ? tokens[amountIndex + 1].Trim()
            : "Uncategorized";

        if (string.IsNullOrWhiteSpace(category) || AmountRegex.IsMatch(category.Replace(" ", "")))
        {
            category = "Uncategorized";
        }

        return new Transaction
        {
            Date = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc),
            Description = description,
            Amount = Math.Abs(amount),
            Category = category,
            UserId = userId
        };
    }

    private Transaction? TryBuildFromLooseLine(string[] tokens, DateTime date, int userId)
    {
        List<(string Token, int Index)> amountCandidates = tokens
            .Skip(1)
            .Select((token, index) => (Token: token, Index: index + 1))
            .Where(item => AmountRegex.IsMatch(item.Token.Replace(" ", "")))
            .ToList();

        if (amountCandidates.Count == 0)
        {
            return null;
        }

        (string token, int index) = amountCandidates.Last();

        if (!TryParseAmount(token, out decimal amount))
        {
            return null;
        }

        if (amount == 0m)
        {
            return null;
        }

        string description = string.Join(" ", tokens.Skip(1).Take(index - 1)).Trim();

        if (string.IsNullOrWhiteSpace(description))
        {
            return null;
        }

        return new Transaction
        {
            Date = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc),
            Description = description,
            Amount = Math.Abs(amount),
            Category = "Uncategorized",
            UserId = userId
        };
    }

    private static bool TryExtractLeadingDateTime(string line, out DateTime date, out string remainingText)
    {
        date = default;
        remainingText = string.Empty;

        Match match = LeadingDateTimeRegex.Match(line);

        if (!match.Success)
        {
            return false;
        }

        string rawDate = match.Groups["date"].Value.Trim().TrimEnd(',');

        if (!TryParseDate(rawDate, out date))
        {
            return false;
        }

        remainingText = line[match.Length..].Trim(' ', '-', '|', ':', '\t');

        return true;
    }

    private static bool TryParseDate(string raw, out DateTime date)
    {
        date = default;

        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        raw = Regex.Replace(raw.Trim(), @"\s+", " ").TrimEnd(',');

        return DateTime.TryParseExact(
                   raw,
                   DateFormats,
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.AllowWhiteSpaces,
                   out date)
               || DateTime.TryParse(
                   raw,
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal,
                   out date);
    }

    private static bool TryParseAmount(string raw, out decimal amount)
    {
        amount = 0m;

        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        raw = raw.Trim();

        bool isNegative = raw.StartsWith('(')
                          || raw.StartsWith('-')
                          || raw.EndsWith("dr", StringComparison.OrdinalIgnoreCase);

        string cleaned = raw
            .Replace("₹", "", StringComparison.OrdinalIgnoreCase)
            .Replace("INR", "", StringComparison.OrdinalIgnoreCase)
            .Replace("Rs.", "", StringComparison.OrdinalIgnoreCase)
            .Replace("Rs", "", StringComparison.OrdinalIgnoreCase)
            .Replace("CR", "", StringComparison.OrdinalIgnoreCase)
            .Replace("DR", "", StringComparison.OrdinalIgnoreCase)
            .Trim('(', ')', '-', '+', ' ');

        int lastComma = cleaned.LastIndexOf(',');
        int lastDot = cleaned.LastIndexOf('.');

        if (lastComma > lastDot)
        {
            cleaned = cleaned.Replace(".", "").Replace(",", ".");
        }
        else
        {
            cleaned = cleaned.Replace(",", "");
        }

        if (!decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out amount))
        {
            return false;
        }

        if (isNegative)
        {
            amount = -amount;
        }

        return true;
    }
}