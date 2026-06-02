using FluentAssertions;
using NUnit.Framework;

[TestFixture]
public class TransactionFiltersTests
{
    [Test]
    public void IsDebit_WhenTransactionIsOutgoing_ReturnsTrue()
    {
        var transaction = new Transaction
        {
            Amount = 500m,
            IsCredit = false,
            Category = "Food",
            Description = "Paid to restaurant"
        };

        TransactionFilters.IsDebit(transaction).Should().BeTrue();
        TransactionFilters.IsCredit(transaction).Should().BeFalse();
    }

    [Test]
    public void IsSpendingAnalytics_WhenCreditTransaction_ReturnsFalse()
    {
        var transaction = new Transaction
        {
            Amount = 2500m,
            IsCredit = true,
            Category = "Salary",
            Description = "Salary credited"
        };

        TransactionFilters.IsSpendingAnalytics(transaction).Should().BeFalse();
    }

    [Test]
    public void IsSpendingAnalytics_WhenTransferCategory_ReturnsFalse()
    {
        var transaction = new Transaction
        {
            Amount = 10000m,
            IsCredit = false,
            Category = "Transfer",
            Description = "Moved money to savings account"
        };

        TransactionFilters.IsSpendingAnalytics(transaction).Should().BeFalse();
    }

    [Test]
    public void IsSpendingAnalytics_WhenSelfTransferDescription_ReturnsFalse()
    {
        var transaction = new Transaction
        {
            Amount = 8000m,
            IsCredit = false,
            Category = "Banking",
            Description = "UPI transfer to self account"
        };

        TransactionFilters.IsSpendingAnalytics(transaction).Should().BeFalse();
    }

    [Test]
    public void IsSpendingAnalytics_WhenRealDebitExpense_ReturnsTrue()
    {
        var transaction = new Transaction
        {
            Amount = 650m,
            IsCredit = false,
            Category = "Food",
            Description = "Paid to cafe"
        };

        TransactionFilters.IsSpendingAnalytics(transaction).Should().BeTrue();
    }
}
