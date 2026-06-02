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
    // Category-level (checked against t.Category, case-insensitive)
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

    // Description-level keywords (substring match, lower-invariant)
    private static readonly string[] TransferDescriptionKeywords =
    {
        "money sent",
        "self transfer",
        "transfer to self",
        "fund transfer",
        "internal transfer",
        "wallet top",          // "Paytm Wallet Top-Up"
        "wallet load",
        "neft",
        "imps",
        "rtgs",
        "to own account",
        "to self",
        "sent to self",
        "upi/",               // raw UPI reference strings like "UPI/12345/TO/..."
    };

    /// <summary>
    /// Returns true if this transaction should be included in spending analytics,
    /// category charts, risk scoring, and insights.
    /// Returns false for credits and for transfers/self-transfers.
    /// </summary>
    public static bool IsSpendingAnalytics(Transaction transaction)
    {
        // Credits are never "spending"
        if (transaction.IsCredit)
            return false;

        string category    = (transaction.Category    ?? string.Empty).Trim();
        string description = (transaction.Description ?? string.Empty).Trim().ToLowerInvariant();

        // Category-level transfer check
        if (TransferCategories.Contains(category))
            return false;

        // Description-level transfer check
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
