using FluentAssertions;
using NUnit.Framework;

[TestFixture]
public class TransactionFiltersTests
{
    // ─── IsDebit / IsCredit ────────────────────────────────────────────────────

    [Test]
    public void IsDebit_WhenIsCreditIsFalse_ReturnsTrue()
    {
        var tx = new Transaction { IsCredit = false, Amount = 500m, Category = "Food", Description = "Lunch" };

        TransactionFilters.IsDebit(tx).Should().BeTrue();
        TransactionFilters.IsCredit(tx).Should().BeFalse();
    }

    [Test]
    public void IsCredit_WhenIsCreditIsTrue_ReturnsTrue()
    {
        var tx = new Transaction { IsCredit = true, Amount = 30000m, Category = "Salary", Description = "Salary credited" };

        TransactionFilters.IsCredit(tx).Should().BeTrue();
        TransactionFilters.IsDebit(tx).Should().BeFalse();
    }

    // ─── IsSpendingAnalytics — must include ───────────────────────────────────

    [Test]
    public void IsSpendingAnalytics_WhenRealDebitExpense_ReturnsTrue()
    {
        var tx = new Transaction
        {
            Amount = 650m,
            IsCredit = false,
            Category = "Food",
            Description = "Paid to Zomato"
        };

        TransactionFilters.IsSpendingAnalytics(tx).Should().BeTrue();
    }

    [Test]
    public void IsSpendingAnalytics_WhenShoppingDebit_ReturnsTrue()
    {
        var tx = new Transaction
        {
            Amount = 2500m,
            IsCredit = false,
            Category = "Shopping",
            Description = "Amazon order"
        };

        TransactionFilters.IsSpendingAnalytics(tx).Should().BeTrue();
    }

    // ─── IsSpendingAnalytics — must exclude credits ───────────────────────────

    [Test]
    public void IsSpendingAnalytics_WhenCredit_ReturnsFalse()
    {
        var tx = new Transaction
        {
            Amount = 50000m,
            IsCredit = true,
            Category = "Salary",
            Description = "Monthly salary"
        };

        TransactionFilters.IsSpendingAnalytics(tx).Should().BeFalse();
    }

    [Test]
    public void IsSpendingAnalytics_WhenCreditTransferIn_ReturnsFalse()
    {
        var tx = new Transaction
        {
            Amount = 1000m,
            IsCredit = true,
            Category = "Transfer",
            Description = "Received from friend"
        };

        TransactionFilters.IsSpendingAnalytics(tx).Should().BeFalse();
    }

    // ─── IsSpendingAnalytics — must exclude transfer categories ──────────────

    [Test]
    [TestCase("Transfer")]
    [TestCase("Self Transfer")]
    [TestCase("Internal Transfer")]
    [TestCase("Fund Transfer")]
    [TestCase("UPI Transfer")]
    [TestCase("Wallet Transfer")]
    [TestCase("Bank Transfer")]
    public void IsSpendingAnalytics_WhenTransferCategory_ReturnsFalse(string category)
    {
        var tx = new Transaction
        {
            Amount = 10000m,
            IsCredit = false,
            Category = category,
            Description = "Moved to savings"
        };

        TransactionFilters.IsSpendingAnalytics(tx).Should().BeFalse();
    }

    // ─── IsSpendingAnalytics — must exclude self-transfer descriptions ────────

    [Test]
    [TestCase("self transfer to savings")]
    [TestCase("UPI/123456/TO/savings")]
    [TestCase("NEFT to HDFC account")]
    [TestCase("IMPS transfer")]
    [TestCase("RTGS payment")]
    [TestCase("fund transfer to own account")]
    [TestCase("sent to self savings")]
    [TestCase("wallet load paytm")]
    [TestCase("wallet top-up")]
    public void IsSpendingAnalytics_WhenDescriptionContainsTransferKeyword_ReturnsFalse(string description)
    {
        var tx = new Transaction
        {
            Amount = 5000m,
            IsCredit = true,
            Category = "Banking",
            Description = description
        };

        TransactionFilters.IsSpendingAnalytics(tx).Should().BeFalse();
    }

    // ─── IsSpendingAnalytics — transfer category is case-insensitive ──────────

    [Test]
    public void IsSpendingAnalytics_WhenTransferCategoryIsUpperCase_ReturnsFalse()
    {
        var tx = new Transaction
        {
            Amount = 3000m,
            IsCredit = false,
            Category = "TRANSFER",
            Description = "Regular payment"
        };

        TransactionFilters.IsSpendingAnalytics(tx).Should().BeFalse();
    }

    // ─── IsSpendingAnalytics — null/empty category edge cases ─────────────────

    [Test]
    public void IsSpendingAnalytics_WhenCategoryIsNullAndDescriptionIsExpense_ReturnsTrue()
    {
        var tx = new Transaction
        {
            Amount = 300m,
            IsCredit = false,
            Category = null!,
            Description = "Grocery purchase"
        };

        TransactionFilters.IsSpendingAnalytics(tx).Should().BeTrue();
    }

    [Test]
    public void IsSpendingAnalytics_WhenCategoryIsEmptyAndDescriptionIsTransfer_ReturnsFalse()
    {
        var tx = new Transaction
        {
            Amount = 1000m,
            IsCredit = true,
            Category = string.Empty,
            Description = "NEFT to savings account"
        };

        TransactionFilters.IsSpendingAnalytics(tx).Should().BeFalse();
    }
}