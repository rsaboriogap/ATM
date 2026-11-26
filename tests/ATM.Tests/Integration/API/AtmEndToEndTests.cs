using Xunit;

namespace ATM.Tests.Integration.Api;

public sealed class AtmEndToEndTests : BackendTestBase
{
    [Fact]
    public async Task CompleteFinancialWorkflow_PreservesIntegrity()
    {
        await using var factory = new AtmApiFactory();
        using var client = CreateClient(factory);

        var accounts = await GetAccountsAsync(client);

        var checking = accounts.Single(
            a => a.Name == "Checking");

        var savings = accounts.Single(
            a => a.Name == "Savings");

        var initialTotal = accounts.Sum(a => a.Balance);

        await DepositAsync(client, checking.Id, 500m);

        await WithdrawAsync(client, checking.Id, 200m);

        var firstTransfer = await TransferAsync(
            client, checking.Id, savings.Id, 300m);

        var secondTransfer = await TransferAsync(
            client, savings.Id, checking.Id, 100m);

        var finalChecking = await GetAccountAsync(
            client, checking.Id);

        var finalSavings = await GetAccountAsync(
            client, savings.Id);

        Assert.Equal(1100m, finalChecking.Balance);
        Assert.Equal(1200m, finalSavings.Balance);

        Assert.Equal(
            initialTotal + 500m - 200m,
            finalChecking.Balance + finalSavings.Balance);

        var checkingHistory = await GetHistoryAsync(
            client, checking.Id);

        var savingsHistory = await GetHistoryAsync(
            client, savings.Id);

        Assert.Equal(4, checkingHistory.Length);
        Assert.Equal(2, savingsHistory.Length);

        var allTransactions = checkingHistory
            .Concat(savingsHistory)
            .ToArray();

        Assert.Equal(6, allTransactions.Length);

        Assert.Equal(
            6,
            allTransactions.Select(t => t.Id).Distinct().Count());

        Assert.NotEqual(
            firstTransfer.ReferenceId,
            secondTransfer.ReferenceId);

        foreach (var transfer in new[]
        {
            firstTransfer,
            secondTransfer
        })
        {
            var relatedTransactions = allTransactions
                .Where(t => t.ReferenceId == transfer.ReferenceId)
                .ToArray();

            Assert.Equal(2, relatedTransactions.Length);

            var outgoing = Assert.Single(
                relatedTransactions.Where(
                    t => t.Type == "TransferOut"));

            var incoming = Assert.Single(
                relatedTransactions.Where(
                    t => t.Type == "TransferIn"));

            Assert.Equal(outgoing.Amount, incoming.Amount);

            Assert.NotEqual(
                outgoing.AccountId,
                incoming.AccountId);
        }
    }

    [Fact]
    public async Task MultipleTransfers_ConserveTotalFunds()
    {
        await using var factory = new AtmApiFactory();
        using var client = CreateClient(factory);

        var accounts = await GetAccountsAsync(client);

        var initialTotal = accounts.Sum(a => a.Balance);

        for (var i = 0; i < 5; i++)
        {
            await TransferAsync(
                client, accounts[0].Id, accounts[1].Id, 100m);

            await TransferAsync(
                client, accounts[1].Id, accounts[0].Id, 50m);
        }

        var finalAccounts = await GetAccountsAsync(client);

        Assert.Equal(
            initialTotal,
            finalAccounts.Sum(a => a.Balance));

        Assert.Equal(
            750m,
            finalAccounts.Single(
                a => a.Id == accounts[0].Id).Balance);

        Assert.Equal(
            1250m,
            finalAccounts.Single(
                a => a.Id == accounts[1].Id).Balance);

        foreach (var account in finalAccounts)
        {
            var history = await GetHistoryAsync(
                client, account.Id);

            Assert.Equal(10, history.Length);
        }
    }
}