/// <summary>
/// Single source of truth for which transactions are counted as real
/// spending (i.e. should appear in category analysis, risk scoring,
/// and insights).
///
/// Rules:
///   1. Credits (incoming money) are never "spending".
///   2. Transactions whose category or description indicate a self-transfer
///      or internal fund movement are excluded from spending analytics
///      because they inflate totals without representing real expenditure.
///
/// IMPORTANT: Both TransactionService and FinancialFeatureExtractor must
/// use this class. Never duplicate this logic.
/// </summary>
public static class TransactionFilters
{
    // ── Transfer detection keywords ──────────────────────────────────────────
    // Category-level (exact match, case-insensitive)
    private static readonly HashSet<string> TransferCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "transfer",
        "self transfer",
        "internal transfer",
        "fund transfer",
        "upi transfer",
        "wallet transfer",
        "bank transfer",
    };

    // Description-level keywords (substring match, case-insensitive).
    //
    // CRITICAL: Do NOT add "upi/" here.
    // Indian bank statements prefix virtually all UPI payments — groceries,
    // restaurants, rent, bills — with "UPI/" (e.g. "UPI/316234/SWIGGY/...").
    // Matching "upi/" would exclude all real spending for the majority of users.
    //
    // Self-transfers via UPI are identified by more specific patterns:
    // "upi self", "upi to self", "upi transfer to" — not the bare "upi/" prefix.
    private static readonly string[] TransferDescriptionKeywords =
    {
        "money sent to self",
        "self transfer",
        "transfer to self",
        "fund transfer to own",
        "internal transfer",
        "wallet top-up",
        "wallet load",
        "to own account",
        "sent to self",
        "upi to self",
        "upi self transfer",
    };

    // NEFT/IMPS/RTGS: these are wire-transfer mechanisms used for both
    // vendor payments AND self-transfers. We do NOT blanket-exclude them
    // because "NEFT to landlord" is a real expense. Only exclude when the
    // description also contains self-transfer indicators (handled above via
    // "transfer to self", "to own account", etc.).

    /// <summary>
    /// Returns true if this transaction should be included in spending analytics,
    /// category charts, risk scoring, and insights.
    /// Returns false for credits and for confirmed self-transfers only.
    /// </summary>
    public static bool IsSpendingAnalytics(Transaction transaction)
    {
        // Credits are never "spending"
        if (transaction.IsCredit)
            return false;

        string category    = (transaction.Category    ?? string.Empty).Trim();
        string description = (transaction.Description ?? string.Empty).Trim().ToLowerInvariant();

        // Category-level transfer check (exact match)
        if (TransferCategories.Contains(category))
            return false;

        // Description-level transfer check (substring match)
        foreach (string keyword in TransferDescriptionKeywords)
        {
            if (description.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Returns true if this transaction is a debit (outgoing payment),
    /// regardless of whether it is a transfer or not.
    /// Use this for TotalSpent which counts ALL outgoing money.
    /// </summary>
    public static bool IsDebit(Transaction transaction) => !transaction.IsCredit;

    /// <summary>
    /// Returns true if this transaction is a credit (incoming money).
    /// </summary>
    public static bool IsCredit(Transaction transaction) => transaction.IsCredit;
}