using ATM.Client.Components.Transactions;
using ATM.Client.DTOs;
using Bunit;
using Xunit;

namespace ATM.Client.Tests.Unit.Components;

public sealed class TransactionHistoryTests : BunitContext
{
    [Fact]
    public void EmptyHistory_DisplaysEmptyState()
    {
        var component = Render<TransactionHistory>();

        Assert.Contains(
            "No transactions yet",
            component.Markup);
    }

    [Theory]
    [InlineData("Deposit", "Deposit", "+$100.00")]
    [InlineData("Withdrawal", "Withdrawal", "-$100.00")]
    [InlineData("TransferIn", "Transfer received", "+$100.00")]
    [InlineData("TransferOut", "Transfer sent", "-$100.00")]
    public void History_DisplaysCorrectTransaction(
        string type,
        string displayName,
        string signedAmount)
    {
        var transaction = new TransactionDto(
            Guid.NewGuid(),
            Guid.NewGuid(),
            type,
            100m,
            1100m,
            null,
            DateTimeOffset.UtcNow);

        var component = Render<TransactionHistory>(
            parameters => parameters
                .Add(p => p.AccountName, "Checking")
                .Add(
                    p => p.Transactions,
                    new[] { transaction }));

        Assert.Contains(displayName, component.Markup);
        Assert.Contains(signedAmount, component.Markup);
        Assert.Contains("$1,100.00", component.Markup);

        Assert.Single(component.FindAll("tbody tr"));
    }
}