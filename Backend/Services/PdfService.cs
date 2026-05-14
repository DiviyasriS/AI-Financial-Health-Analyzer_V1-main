using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

/// <summary>
/// Parses PDF bank statements into Transaction objects.
///
/// Supported formats
/// ─────────────────
/// The parser works on two structural patterns that cover the vast majority
/// of exported bank-statement PDFs:
///
///   Pattern A – pure tabular text (most common)
///     Each page contains lines like:
///       01/04/2025  Amazon Purchase        -1250.00  Shopping
///       03/04/2025  Salary Credit          50000.00  Income
///     Columns are delimited by 2+ spaces (or a tab).  The parser tries
///     several common date formats and multiple column orderings.
///
///   Pattern B – two-column PDF layout
///     Some banks render text in two visual columns; PdfPig returns words
///     in reading order which produces interleaved lines.  The parser
///     groups words by Y-coordinate (±3 px tolerance) into logical lines
///     before applying Pattern-A rules.
///
/// Robustness
/// ──────────
/// • Skips header rows, totals rows, and "running balance" columns.
/// • Handles amounts formatted as 1,234.56 / 1.234,56 / (1250.00).
/// • Skips zero-amount and purely whitespace rows.
/// • Skips encrypted / image-only PDFs gracefully (logs a warning).
/// • All parsing errors are caught per-row; malformed rows are counted as
///   skipped rather than crashing the entire upload.
/// </summary>
public class PdfService
{
    private readonly ILogger<PdfService> _logger;

    // ── Date formats tried in order ──────────────────────────────────────
    private static readonly string[] DateFormats =
    {
        "dd/MM/yyyy", "d/M/yyyy",
        "MM/dd/yyyy", "M/d/yyyy",
        "yyyy-MM-dd", "yyyy/MM/dd",
        "dd-MM-yyyy", "d-M-yyyy",
        "dd MMM yyyy", "d MMM yyyy",
        "dd-MMM-yyyy", "d-MMM-yyyy",
        "MMM dd yyyy", "MMM d yyyy",
        "dd/MM/yy",   "d/M/yy",
        "MM/dd/yy",   "M/d/yy"
    };

    // ── Regex for amount extraction ──────────────────────────────────────
    // Matches: 1234.56 | 1,234.56 | 1.234,56 | (1,234.56) | -1234.56 | 1,23,456.00
    private static readonly Regex AmountRegex = new(
        @"^[(\-]?\s*[\d,\.]+\s*\)?$",
        RegexOptions.Compiled);

    // ── Keywords that flag a row as a non-transaction line ───────────────
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

    // ────────────────────────────────────────────────────────────────────
    // Public entry point
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Parses a PDF stream and returns a <see cref="ParsedFileResult"/>
    /// using exactly the same model as <see cref="CsvService"/> and
    /// <see cref="XlsxService"/> so the caller (TransactionService) needs
    /// no changes.
    /// </summary>
    public Task<ParsedFileResult> ParseAsync(Stream fileStream, int userId)
    {
        var result = new ParsedFileResult();

        // ── 1. Validate the stream ────────────────────────────────────
        if (fileStream == null || fileStream.Length == 0)
        {
            _logger.LogWarning("PDF upload for user {UserId}: empty stream.", userId);
            return Task.FromResult(result);
        }

        // ── 2. Open with PdfPig ───────────────────────────────────────
        PdfDocument? document = null;
        try
        {
            document = PdfDocument.Open(fileStream);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "PDF upload for user {UserId}: cannot open PDF — may be encrypted or corrupt.",
                userId);
            return Task.FromResult(result);
        }

        using (document)
        {
            if (document.NumberOfPages == 0)
            {
                _logger.LogWarning(
                    "PDF upload for user {UserId}: document has no pages.", userId);
                return Task.FromResult(result);
            }

            // ── 3. Extract text lines from every page ─────────────────
            var allLines = ExtractLinesFromDocument(document, userId);

            if (allLines.Count == 0)
            {
                _logger.LogWarning(
                    "PDF upload for user {UserId}: no text could be extracted " +
                    "(image-based PDF?)", userId);
                return Task.FromResult(result);
            }

            _logger.LogInformation(
                "PDF upload for user {UserId}: extracted {LineCount} candidate lines.",
                userId, allLines.Count);

            // ── 4. Parse each line ────────────────────────────────────
            bool headerSeen = false;

            foreach (string rawLine in allLines)
            {
                string line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;

                // Skip obvious header / footer rows
                if (IsHeaderOrFooterRow(line))
                {
                    headerSeen = true;
                    continue;
                }

                result.TotalRowsFound++;

                Transaction? tx = TryParseLine(line, userId, headerSeen);
                if (tx != null)
                {
                    result.Transactions.Add(tx);
                }
                else
                {
                    result.SkippedRows++;
                    _logger.LogDebug(
                        "PDF user {UserId}: skipped row — could not parse: [{Line}]",
                        userId, line);
                }
            }
        }

        _logger.LogInformation(
            "PDF parse complete for user {UserId}: " +
            "{Total} rows found, {Valid} valid, {Skipped} skipped.",
            userId,
            result.TotalRowsFound,
            result.Transactions.Count,
            result.SkippedRows);

        return Task.FromResult(result);
    }

    // ────────────────────────────────────────────────────────────────────
    // Text extraction
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Extracts logical text lines from all pages of the document.
    /// Groups words by their Y-coordinate (±3 px tolerance) so that
    /// two-column PDFs produce correct left-to-right lines.
    /// </summary>
    private List<string> ExtractLinesFromDocument(PdfDocument document, int userId)
    {
        var allLines = new List<string>();

        for (int pageNum = 1; pageNum <= document.NumberOfPages; pageNum++)
        {
            try
            {
                Page page = document.GetPage(pageNum);
                IReadOnlyList<Word> words = page.GetWords().ToList();

                if (words.Count == 0) continue;

                // Group words into lines by Y-coordinate (round to nearest 3 px)
                var lineGroups = words
                    .GroupBy(w => (int)Math.Round(w.BoundingBox.Top / 3.0) * 3)
                    .OrderByDescending(g => g.Key);   // top of page = highest Y

                foreach (var group in lineGroups)
                {
                    // Within a line, order words left→right
                    string line = string.Join(" ",
                        group
                            .OrderBy(w => w.BoundingBox.Left)
                            .Select(w => w.Text));

                    if (!string.IsNullOrWhiteSpace(line))
                        allLines.Add(line);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "PDF user {UserId}: failed to read page {Page}.", userId, pageNum);
            }
        }

        return allLines;
    }

    // ────────────────────────────────────────────────────────────────────
    // Line classification helpers
    // ────────────────────────────────────────────────────────────────────

    private static bool IsHeaderOrFooterRow(string line)
    {
        string lower = line.ToLowerInvariant();

        // If the line contains only header keywords (and no digit sequences
        // that look like amounts), treat it as a header.
        if (SkipKeywords.Any(kw => lower.Contains(kw)) &&
            !Regex.IsMatch(line, @"\d{1,2}[/\-]\d{1,2}[/\-]\d{2,4}"))
        {
            return true;
        }

        // Lines with very few characters are likely page separators
        if (line.Replace(" ", "").Length < 5) return true;

        // Pure numeric lines (page numbers, etc.)
        if (Regex.IsMatch(line.Trim(), @"^\d{1,3}$")) return true;

        return false;
    }

    // ────────────────────────────────────────────────────────────────────
    // Core line parser
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Tries to parse a single text line into a <see cref="Transaction"/>.
    /// Returns null if the line cannot be interpreted as a transaction.
    ///
    /// Column-order strategies tried (in priority order):
    ///   1. Date | Description | Amount | Category   (4+ tokens)
    ///   2. Date | Description | Amount              (3 tokens, no category)
    ///   3. Date | Amount | Description              (amount before description)
    /// </summary>
    private Transaction? TryParseLine(string line, int userId, bool headerSeen)
    {
        // Split on 2+ whitespace chars (tab or multiple spaces)
        string[] tokens = Regex.Split(line, @"\s{2,}|\t")
                               .Select(t => t.Trim())
                               .Where(t => !string.IsNullOrWhiteSpace(t))
                               .ToArray();

        if (tokens.Length < 2) return null;

        // ── Strategy 1: first token is a date ────────────────────────
        if (TryParseDate(tokens[0], out DateTime date))
        {
            return TryBuildFromDateFirst(tokens, date, userId);
        }

        // ── Strategy 2: second token is a date (bank name in col 0) ──
        if (tokens.Length >= 3 && TryParseDate(tokens[1], out date))
        {
            // shift: treat tokens[1..] as a date-first row
            string[] shifted = tokens.Skip(1).ToArray();
            return TryBuildFromDateFirst(shifted, date, userId);
        }

        // ── Strategy 3: fall back to single-space split ───────────────
        string[] singleSplit = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (singleSplit.Length >= 3 && TryParseDate(singleSplit[0], out date))
        {
            // Try to find an amount token and treat everything between
            // date and amount as description
            return TryBuildFromLooseLine(singleSplit, date, userId);
        }

        return null;
    }

    /// <summary>
    /// Builds a transaction when tokens[0] is already parsed as <paramref name="date"/>.
    /// </summary>
    private Transaction? TryBuildFromDateFirst(string[] tokens, DateTime date, int userId)
    {
        // Need at least date + description + amount
        if (tokens.Length < 2) return null;

        // Find the rightmost amount-like token (to tolerate running-balance column)
        // We check from right to left, skipping the last token if there are 4+
        // (last column is often the running balance we want to ignore).
        int searchEnd = tokens.Length >= 4 ? tokens.Length - 1 : tokens.Length;

        int amountIdx = -1;
        for (int i = searchEnd - 1; i >= 1; i--)
        {
            if (AmountRegex.IsMatch(tokens[i].Replace(" ", "")))
            {
                amountIdx = i;
                break;
            }
        }

        if (amountIdx < 0) return null;

        if (!TryParseAmount(tokens[amountIdx], out decimal amount)) return null;
        if (amount == 0m) return null;

        // Description = everything between date and amount
        string description = string.Join(" ", tokens.Skip(1).Take(amountIdx - 1)).Trim();
        if (string.IsNullOrWhiteSpace(description)) return null;

        // Category = token after amount (if present)
        string category = amountIdx + 1 < tokens.Length
            ? tokens[amountIdx + 1].Trim()
            : "Uncategorized";

        if (string.IsNullOrWhiteSpace(category) ||
            AmountRegex.IsMatch(category.Replace(" ", "")))
        {
            category = "Uncategorized";
        }

        return new Transaction
        {
            Date        = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc),
            Description = description,
            Amount      = Math.Abs(amount),   // store as positive; sign is lost in bank PDFs
            Category    = category,
            UserId      = userId
        };
    }

    /// <summary>
    /// Fallback parser for single-space-split tokens.
    /// Scans all tokens for an amount and treats the rest as description.
    /// </summary>
    private Transaction? TryBuildFromLooseLine(string[] tokens, DateTime date, int userId)
    {
        // Skip token[0] (date already parsed)
        var amountCandidates = tokens
            .Skip(1)
            .Select((t, i) => (Token: t, Index: i + 1))
            .Where(x => AmountRegex.IsMatch(x.Token.Replace(" ", "")))
            .ToList();

        if (amountCandidates.Count == 0) return null;

        // Prefer the first amount we find (skip running balance at end)
        var (token, idx) = amountCandidates.First();
        if (!TryParseAmount(token, out decimal amount)) return null;
        if (amount == 0m) return null;

        // Everything between date and amount = description
        var descTokens = tokens.Skip(1).Take(idx - 1).ToList();
        string description = string.Join(" ", descTokens).Trim();
        if (string.IsNullOrWhiteSpace(description)) return null;

        return new Transaction
        {
            Date        = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc),
            Description = description,
            Amount      = Math.Abs(amount),
            Category    = "Uncategorized",
            UserId      = userId
        };
    }

    // ────────────────────────────────────────────────────────────────────
    // Date / Amount helpers
    // ────────────────────────────────────────────────────────────────────

    private static bool TryParseDate(string raw, out DateTime date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        raw = raw.Trim();

        return DateTime.TryParseExact(
                   raw, DateFormats,
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.None,
                   out date)
               || DateTime.TryParse(
                   raw,
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.AssumeLocal,
                   out date);
    }

    /// <summary>
    /// Parses amount strings such as:
    ///   1234.56 | 1,234.56 | 1.234,56 | (1,234.56) | -1234.56
    /// Always returns a positive value; negative is ignored (we store
    /// absolute values consistent with CSV/XLSX parsers).
    /// </summary>
    private static bool TryParseAmount(string raw, out decimal amount)
    {
        amount = 0m;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        // Remove parentheses (bank notation for debit)
        bool isNegative = raw.StartsWith('(') || raw.StartsWith('-');
        string cleaned  = raw.Trim('(', ')', '-', '+', ' ');

        // Detect European format: last separator is a comma (e.g., 1.234,56)
        int lastComma = cleaned.LastIndexOf(',');
        int lastDot   = cleaned.LastIndexOf('.');

        if (lastComma > lastDot)
        {
            // European: replace dots (thousands sep) and comma (decimal sep)
            cleaned = cleaned.Replace(".", "").Replace(",", ".");
        }
        else
        {
            // US/Indian: remove commas (thousands separators)
            cleaned = cleaned.Replace(",", "");
        }

        if (!decimal.TryParse(cleaned, NumberStyles.Any,
                CultureInfo.InvariantCulture, out amount))
        {
            return false;
        }

        if (isNegative) amount = -amount;
        return true;
    }
}
