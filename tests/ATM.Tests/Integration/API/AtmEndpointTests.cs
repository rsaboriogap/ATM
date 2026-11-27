using System.Net;
using System.Net.Http.Json;
using ATM.Application.DTOs;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using ATM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ATM.Tests.Integration.Api;

public sealed class AtmEndpointTests
{
    private static HttpClient CreateClient(
        AtmApiFactory factory)
    {
        return factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false
            });
    }



    private static async Task<AccountDto[]> GetAccountsAsync(
        HttpClient client)
    {
        return await client.GetFromJsonAsync<AccountDto[]>(
            "/api/accounts")
            ?? throw new InvalidOperationException(
                "Accounts response was empty.");
    }

    [Fact]
    public async Task Factory_UsesIsolatedDatabase()
    {
        await using var factory = new AtmApiFactory();
        using var client = CreateClient(factory);

        await using var scope =
            factory.Services.CreateAsyncScope();

        var context = scope.ServiceProvider
            .GetRequiredService<AtmDbContext>();

        var connection = context.Database.GetDbConnection();

        Assert.Equal(
            Path.GetFullPath(factory.DatabasePath),
            Path.GetFullPath(connection.DataSource));

        Assert.True(File.Exists(factory.DatabasePath));
    }


    [Fact]
    public async Task Factories_UseIndependentDatabases()
    {
        await using var factory1 = new AtmApiFactory();
        await using var factory2 = new AtmApiFactory();

        using var client1 = CreateClient(factory1);
        using var client2 = CreateClient(factory2);

        var accounts1 = await GetAccountsAsync(client1);
        var accounts2 = await GetAccountsAsync(client2);

        Assert.NotEqual(
            factory1.DatabasePath,
            factory2.DatabasePath);

        var response = await client1.PostAsJsonAsync(
            $"/api/accounts/{accounts1[0].Id}/deposits",
            new { Amount = 500m });

        response.EnsureSuccessStatusCode();

        var updatedAccounts1 =
            await GetAccountsAsync(client1);

        var updatedAccounts2 =
            await GetAccountsAsync(client2);

        Assert.Equal(1500m, updatedAccounts1[0].Balance);
        Assert.Equal(1000m, updatedAccounts2[0].Balance);
    }


    [Fact]
    public async Task Factory_InitializesCleanDatabase()
    {
        await using var factory = new AtmApiFactory();
        using var client = CreateClient(factory);

        var accounts = await GetAccountsAsync(client);

        Assert.Equal(2, accounts.Length);

        Assert.All(accounts, account =>
        {
            Assert.Equal(1000m, account.Balance);
        });

        foreach (var account in accounts)
        {
            // A clean database holds exactly one OpeningBalance entry per
            // account, matching the seeded balance, and nothing else.
            var ledger = await client
                .GetFromJsonAsync<TransactionDto[]>(
                    $"/api/accounts/{account.Id}/transactions");

            Assert.NotNull(ledger);

            var opening = Assert.Single(ledger);

            Assert.Equal("OpeningBalance", opening.Type);
            Assert.Equal(1000m, opening.Amount);
            Assert.Equal(1000m, opening.BalanceAfter);
        }
    }



    [Fact]
    public async Task GetAccounts_ReturnsTwoAccounts()
    {
        await using var factory = new AtmApiFactory();
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/api/accounts");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var accounts = await response.Content
            .ReadFromJsonAsync<AccountDto[]>();

        Assert.NotNull(accounts);
        Assert.Equal(2, accounts.Length);
        Assert.All(accounts, a => Assert.Equal(1000m, a.Balance));
    }

    [Fact]
    public async Task GetAccount_ReturnsRequestedAccount()
    {
        await using var factory = new AtmApiFactory();
        using var client = CreateClient(factory);

        var accounts = await GetAccountsAsync(client);
        var expected = accounts[0];

        var response = await client.GetAsync(
            $"/api/accounts/{expected.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var account = await response.Content
            .ReadFromJsonAsync<AccountDto>();

        Assert.NotNull(account);
        Assert.Equal(expected.Id, account.Id);
        Assert.Equal(expected.Balance, account.Balance);
    }

    [Fact]
    public async Task Deposit_ReturnsUpdatedBalance()
    {
        await using var factory = new AtmApiFactory();
        using var client = CreateClient(factory);

        var accounts = await GetAccountsAsync(client);
        var account = accounts[0];

        var response = await client.PostAsJsonAsync(
            $"/api/accounts/{account.Id}/deposits",
            new { Amount = 150m });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content
            .ReadFromJsonAsync<OperationResultDto>();

        Assert.NotNull(result);
        Assert.Equal(1150m, result.Account.Balance);
        Assert.Equal("Deposit", result.Transaction.Type);

        var persisted = await client.GetFromJsonAsync<AccountDto>(
            $"/api/accounts/{account.Id}");

        Assert.NotNull(persisted);
        Assert.Equal(1150m, persisted.Balance);
    }

    [Fact]
    public async Task Withdraw_ReturnsUpdatedBalance()
    {
        await using var factory = new AtmApiFactory();
        using var client = CreateClient(factory);

        var accounts = await GetAccountsAsync(client);
        var account = accounts[0];

        var response = await client.PostAsJsonAsync(
            $"/api/accounts/{account.Id}/withdrawals",
            new { Amount = 250m });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content
            .ReadFromJsonAsync<OperationResultDto>();

        Assert.NotNull(result);
        Assert.Equal(750m, result.Account.Balance);
        Assert.Equal("Withdrawal", result.Transaction.Type);
    }

    [Fact]
    public async Task Transfer_ReturnsBothUpdatedAccounts()
    {
        await using var factory = new AtmApiFactory();
        using var client = CreateClient(factory);

        var accounts = await GetAccountsAsync(client);

        var response = await client.PostAsJsonAsync(
            "/api/transfers",
            new
            {
                SourceAccountId = accounts[0].Id,
                DestinationAccountId = accounts[1].Id,
                Amount = 300m
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content
            .ReadFromJsonAsync<TransferResultDto>();

        Assert.NotNull(result);

        Assert.Equal(700m, result.SourceAccount.Balance);
        Assert.Equal(1300m, result.DestinationAccount.Balance);

        Assert.Equal(
            result.ReferenceId,
            result.OutgoingTransaction.ReferenceId);

        Assert.Equal(
            result.ReferenceId,
            result.IncomingTransaction.ReferenceId);
    }

    [Fact]
    public async Task GetHistory_ReturnsAccountTransactions()
    {
        await using var factory = new AtmApiFactory();
        using var client = CreateClient(factory);

        var accounts = await GetAccountsAsync(client);
        var account = accounts[0];

        var depositResponse = await client.PostAsJsonAsync(
            $"/api/accounts/{account.Id}/deposits",
            new { Amount = 100m });

        depositResponse.EnsureSuccessStatusCode();

        var response = await client.GetAsync(
            $"/api/accounts/{account.Id}/transactions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var history = await response.Content
            .ReadFromJsonAsync<TransactionDto[]>();

        Assert.NotNull(history);
        Assert.Equal(2, history.Length);

        Assert.Single(history, t => t.Type == "OpeningBalance");

        var deposit = Assert.Single(
            history, t => t.Type == "Deposit");

        Assert.Equal(1100m, deposit.BalanceAfter);
    }

    [Fact]
    public async Task Deposit_MissingAmount_ReturnsBadRequest()
    {
        await using var factory = new AtmApiFactory();
        using var client = CreateClient(factory);

        var accounts = await GetAccountsAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/accounts/{accounts[0].Id}/deposits",
            new { });

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
    }

    [Fact]
    public async Task Deposit_InvalidDataType_ReturnsBadRequest()
    {
        await using var factory = new AtmApiFactory();
        using var client = CreateClient(factory);

        var accounts = await GetAccountsAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/accounts/{accounts[0].Id}/deposits",
            new { Amount = "invalid" });

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
    }

    [Fact]
    public async Task Transfer_MissingDestination_ReturnsBadRequest()
    {
        await using var factory = new AtmApiFactory();
        using var client = CreateClient(factory);

        var accounts = await GetAccountsAsync(client);

        var response = await client.PostAsJsonAsync(
            "/api/transfers",
            new
            {
                SourceAccountId = accounts[0].Id,
                Amount = 100m
            });

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
    }

    [Fact]
    public async Task GetAccount_InvalidGuid_ReturnsNotFound()
    {
        await using var factory = new AtmApiFactory();
        using var client = CreateClient(factory);

        var response = await client.GetAsync(
            "/api/accounts/invalid-guid");

        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);
    }

    [Fact]
    public async Task OpenApi_ReturnsDocument()
    {
        await using var factory = new AtmApiFactory();
        using var client = CreateClient(factory);

        var response = await client.GetAsync(
            "/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var document = await response.Content.ReadAsStringAsync();

        Assert.Contains("/api/accounts", document);
        Assert.Contains("/api/transfers", document);
    }
}