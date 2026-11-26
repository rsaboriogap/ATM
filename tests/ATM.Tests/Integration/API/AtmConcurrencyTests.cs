using System.Net;
using System.Net.Http.Json;
using ATM.Api.ErrorHandling;
using Xunit;

namespace ATM.Tests.Integration.Api;

public sealed class AtmConcurrencyTests : BackendTestBase
{
    [Fact]
    public async Task ConcurrentWithdrawals_PreventOverdraft()
    {
        await using var factory = new AtmApiFactory(
            defaultTimeout: 15);

        using var client = CreateClient(factory);

        var accounts = await GetAccountsAsync(client);
        var account = accounts[0];

        var requests = Enumerable.Range(0, 2)
            .Select(_ => client.PostAsJsonAsync(
                $"/api/accounts/{account.Id}/withdrawals",
                new { Amount = 800m }))
            .ToArray();

        var responses = await Task.WhenAll(requests);

        try
        {
            Assert.Single(responses.Where(
                r => r.StatusCode == HttpStatusCode.OK));

            var rejected = Assert.Single(responses.Where(
                r => r.StatusCode == HttpStatusCode.Conflict));

            await AssertProblemAsync(
                rejected,
                HttpStatusCode.Conflict,
                ErrorCodes.InsufficientFunds);

            var updated = await GetAccountAsync(
                client, account.Id);

            Assert.Equal(200m, updated.Balance);

            var history = await GetHistoryAsync(
                client, account.Id);

            Assert.Single(history);
            Assert.Equal("Withdrawal", history[0].Type);
            Assert.Equal(800m, history[0].Amount);
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task ConcurrentDeposits_DoNotLoseUpdates()
    {
        await using var factory = new AtmApiFactory(
            defaultTimeout: 15);

        using var client = CreateClient(factory);

        var accounts = await GetAccountsAsync(client);
        var account = accounts[0];

        const int operationCount = 10;
        const decimal amount = 10m;

        var requests = Enumerable.Range(0, operationCount)
            .Select(_ => DepositAsync(
                client, account.Id, amount))
            .ToArray();

        await Task.WhenAll(requests);

        var updated = await GetAccountAsync(
            client, account.Id);

        Assert.Equal(
            account.Balance + operationCount * amount,
            updated.Balance);

        var history = await GetHistoryAsync(
            client, account.Id);

        Assert.Equal(operationCount, history.Length);

        Assert.Equal(
            operationCount * amount,
            history.Sum(t => t.Amount));

        Assert.Equal(
            operationCount,
            history.Select(t => t.Id).Distinct().Count());
    }

    [Fact]
    public async Task ConcurrentTransfers_ConserveTotalFunds()
    {
        await using var factory = new AtmApiFactory(
            defaultTimeout: 15);

        using var client = CreateClient(factory);

        var accounts = await GetAccountsAsync(client);

        var source = accounts[0];
        var destination = accounts[1];

        const int operationCount = 10;
        const decimal amount = 50m;

        var requests = Enumerable.Range(0, operationCount)
            .Select(_ => TransferAsync(
                client,
                source.Id,
                destination.Id,
                amount))
            .ToArray();

        var results = await Task.WhenAll(requests);

        var updatedSource = await GetAccountAsync(
            client, source.Id);

        var updatedDestination = await GetAccountAsync(
            client, destination.Id);

        Assert.Equal(500m, updatedSource.Balance);
        Assert.Equal(1500m, updatedDestination.Balance);

        Assert.Equal(
            2000m,
            updatedSource.Balance + updatedDestination.Balance);

        Assert.Equal(
            operationCount,
            results.Select(r => r.ReferenceId).Distinct().Count());

        var sourceHistory = await GetHistoryAsync(
            client, source.Id);

        var destinationHistory = await GetHistoryAsync(
            client, destination.Id);

        Assert.Equal(operationCount, sourceHistory.Length);
        Assert.Equal(operationCount, destinationHistory.Length);

        foreach (var result in results)
        {
            var outgoing = Assert.Single(
                sourceHistory.Where(
                    t => t.ReferenceId == result.ReferenceId));

            var incoming = Assert.Single(
                destinationHistory.Where(
                    t => t.ReferenceId == result.ReferenceId));

            Assert.Equal("TransferOut", outgoing.Type);
            Assert.Equal("TransferIn", incoming.Type);
            Assert.Equal(outgoing.Amount, incoming.Amount);
        }
    }
}