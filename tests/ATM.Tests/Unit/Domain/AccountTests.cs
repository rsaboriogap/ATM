using ATM.Domain.Entities;
using ATM.Domain.Enums;
using ATM.Domain.Exceptions;
using ATM.Domain.Rules;
using Xunit;

namespace ATM.Tests.Unit.Domain;

public sealed class AccountTests
{
    private static Account NewAccount(decimal balance = 1000m) =>
        Account.Open("Checking", balance).Account;

    [Fact]
    public void Open_ValidAccount_SetsInitialBalance()
    {
        var (account, _) = Account.Open("Checking", 1000m);

        Assert.NotEqual(Guid.Empty, account.Id);
        Assert.Equal("Checking", account.Name);
        Assert.Equal(1000m, account.Balance);
        Assert.Equal("USD", account.Currency);
    }

    [Fact]
    public void Open_ValidAccount_ReturnsOpeningBalanceEntry()
    {
        var (account, openingEntry) = Account.Open("Checking", 1000m);

        Assert.Equal(account.Id, openingEntry.AccountId);
        Assert.Equal(TransactionType.OpeningBalance, openingEntry.Type);
        Assert.Equal(1000m, openingEntry.Amount);
        Assert.Equal(1000m, openingEntry.BalanceAfter);
        Assert.Null(openingEntry.ReferenceId);
        Assert.Equal(1, openingEntry.Sequence);
        Assert.Equal(1, account.LedgerSequence);
    }

    [Fact]
    public void Operations_AssignConsecutiveSequences()
    {
        var source = Account.Open("Checking", 1000m).Account;
        var destination = Account.Open("Savings", 500m).Account;

        var deposit = source.Deposit(10m);
        var withdrawal = source.Withdraw(5m);
        var transfer = Account.Transfer(source, destination, 1m);

        Assert.Equal(2, deposit.Sequence);
        Assert.Equal(3, withdrawal.Sequence);
        Assert.Equal(4, transfer.Outgoing.Sequence);
        Assert.Equal(2, transfer.Incoming.Sequence);
        Assert.Equal(4, source.LedgerSequence);
        Assert.Equal(2, destination.LedgerSequence);
    }

    [Fact]
    public void FailedOperations_DoNotAdvanceSequence()
    {
        var source = Account.Open("Checking", 100m).Account;
        var destination = Account.Open("Savings", 500m).Account;

        Assert.Throws<InsufficientFundsException>(() => source.Withdraw(101m));
        Assert.Throws<InvalidAmountException>(() => source.Deposit(0m));
        Assert.Throws<InsufficientFundsException>(
            () => Account.Transfer(source, destination, 200m));

        Assert.Equal(1, source.LedgerSequence);
        Assert.Equal(1, destination.LedgerSequence);
        Assert.Equal(2, source.Deposit(1m).Sequence);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-100")]
    public void Open_NonPositiveBalance_ThrowsException(string value)
    {
        var balance = decimal.Parse(
            value,
            System.Globalization.CultureInfo.InvariantCulture);

        Assert.Throws<InvalidAmountException>(
            () => Account.Open("Checking", balance));
    }

    [Fact]
    public void Deposit_ValidAmount_IncreasesBalanceAndReturnsEntry()
    {
        var account = NewAccount();

        var entry = account.Deposit(150.50m);

        Assert.Equal(1150.50m, account.Balance);
        Assert.Equal(account.Id, entry.AccountId);
        Assert.Equal(TransactionType.Deposit, entry.Type);
        Assert.Equal(150.50m, entry.Amount);
        Assert.Equal(1150.50m, entry.BalanceAfter);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-100")]
    [InlineData("0.001")]
    [InlineData("1000000.01")]
    public void Deposit_InvalidAmount_PreservesBalance(
        string value)
    {
        var amount = decimal.Parse(
            value,
            System.Globalization.CultureInfo.InvariantCulture);

        var account = NewAccount();

        Assert.Throws<InvalidAmountException>(
            () => account.Deposit(amount));

        Assert.Equal(1000m, account.Balance);
    }

    [Fact]
    public void Deposit_ExceedsMaximumBalance_ThrowsException()
    {
        // Opening balance is limited to MaximumAmount, so reach the
        // maximum balance through deposits.
        var account = NewAccount(MoneyRules.MaximumAmount);

        while (account.Balance < MoneyRules.MaximumBalance)
        {
            account.Deposit(MoneyRules.MaximumAmount);
        }

        Assert.Throws<BalanceLimitExceededException>(
            () => account.Deposit(1m));

        Assert.Equal(
            MoneyRules.MaximumBalance,
            account.Balance);
    }

    [Fact]
    public void Withdraw_SufficientFunds_DecreasesBalanceAndReturnsEntry()
    {
        var account = NewAccount();

        var entry = account.Withdraw(250m);

        Assert.Equal(750m, account.Balance);
        Assert.Equal(TransactionType.Withdrawal, entry.Type);
        Assert.Equal(250m, entry.Amount);
        Assert.Equal(750m, entry.BalanceAfter);
    }

    [Fact]
    public void Withdraw_ExactBalance_LeavesZeroBalance()
    {
        var account = NewAccount();

        account.Withdraw(1000m);

        Assert.Equal(0m, account.Balance);
    }

    [Fact]
    public void Withdraw_InsufficientFunds_PreservesBalance()
    {
        var account = NewAccount();

        Assert.Throws<InsufficientFundsException>(
            () => account.Withdraw(1001m));

        Assert.Equal(1000m, account.Balance);
    }

    [Fact]
    public void Withdraw_NegativeAmount_PreservesBalance()
    {
        var account = NewAccount();

        Assert.Throws<InvalidAmountException>(
            () => account.Withdraw(-100m));

        Assert.Equal(1000m, account.Balance);
    }

    [Fact]
    public void Transfer_Valid_MovesFundsAndReturnsLinkedEntries()
    {
        var source = Account.Open("Checking", 1000m).Account;
        var destination = Account.Open("Savings", 500m).Account;

        var entries = Account.Transfer(source, destination, 200m);

        Assert.Equal(800m, source.Balance);
        Assert.Equal(700m, destination.Balance);

        Assert.NotEqual(Guid.Empty, entries.ReferenceId);
        Assert.Equal(TransactionType.TransferOut, entries.Outgoing.Type);
        Assert.Equal(source.Id, entries.Outgoing.AccountId);
        Assert.Equal(800m, entries.Outgoing.BalanceAfter);
        Assert.Equal(TransactionType.TransferIn, entries.Incoming.Type);
        Assert.Equal(destination.Id, entries.Incoming.AccountId);
        Assert.Equal(700m, entries.Incoming.BalanceAfter);
        Assert.Equal(entries.ReferenceId, entries.Outgoing.ReferenceId);
        Assert.Equal(entries.ReferenceId, entries.Incoming.ReferenceId);
    }

    [Fact]
    public void Transfer_InsufficientFunds_PreservesBothBalances()
    {
        var source = Account.Open("Checking", 100m).Account;
        var destination = Account.Open("Savings", 500m).Account;

        Assert.Throws<InsufficientFundsException>(
            () => Account.Transfer(source, destination, 200m));

        Assert.Equal(100m, source.Balance);
        Assert.Equal(500m, destination.Balance);
    }

    [Fact]
    public void Transfer_SameAccount_ThrowsException()
    {
        var account = NewAccount();

        Assert.Throws<ArgumentException>(
            () => Account.Transfer(account, account, 10m));
    }
}
