using ATM.Domain.Entities;
using ATM.Domain.Exceptions;
using ATM.Domain.Rules;
using Xunit;

namespace ATM.Tests.Unit.Domain;

public sealed class AccountTests
{
    [Fact]
    public void Create_ValidAccount_SetsInitialBalance()
    {
        var account = Account.Create("Checking", 1000m);

        Assert.NotEqual(Guid.Empty, account.Id);
        Assert.Equal("Checking", account.Name);
        Assert.Equal(1000m, account.Balance);
        Assert.Equal("USD", account.Currency);
    }

    [Fact]
    public void Deposit_ValidAmount_IncreasesBalance()
    {
        // Arrange
        var account = Account.Create("Checking", 1000m);

        // Act
        account.Deposit(150.50m);

        // Assert
        Assert.Equal(1150.50m, account.Balance);
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

        var account = Account.Create("Checking", 1000m);

        Assert.Throws<InvalidAmountException>(
            () => account.Deposit(amount));

        Assert.Equal(1000m, account.Balance);
    }

    [Fact]
    public void Deposit_ExceedsMaximumBalance_ThrowsException()
    {
        var account = Account.Create(
            "Checking",
            MoneyRules.MaximumBalance);

        Assert.Throws<BalanceLimitExceededException>(
            () => account.Deposit(1m));

        Assert.Equal(
            MoneyRules.MaximumBalance,
            account.Balance);
    }

    [Fact]
    public void Withdraw_SufficientFunds_DecreasesBalance()
    {
        var account = Account.Create("Checking", 1000m);

        account.Withdraw(250m);

        Assert.Equal(750m, account.Balance);
    }

    [Fact]
    public void Withdraw_ExactBalance_LeavesZeroBalance()
    {
        var account = Account.Create("Checking", 1000m);

        account.Withdraw(1000m);

        Assert.Equal(0m, account.Balance);
    }

    [Fact]
    public void Withdraw_InsufficientFunds_PreservesBalance()
    {
        var account = Account.Create("Checking", 1000m);

        Assert.Throws<InsufficientFundsException>(
            () => account.Withdraw(1001m));

        Assert.Equal(1000m, account.Balance);
    }

    [Fact]
    public void Withdraw_NegativeAmount_PreservesBalance()
    {
        var account = Account.Create("Checking", 1000m);

        Assert.Throws<InvalidAmountException>(
            () => account.Withdraw(-100m));

        Assert.Equal(1000m, account.Balance);
    }

    [Fact]
    public void Create_NegativeBalance_ThrowsException()
    {
        Assert.Throws<InvalidAmountException>(
            () => Account.Create("Checking", -100m));
    }
}