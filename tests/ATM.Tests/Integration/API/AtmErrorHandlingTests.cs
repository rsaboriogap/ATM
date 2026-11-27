using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ATM.Api.ErrorHandling;
using ATM.Application.DTOs;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using ATM.Application.Interfaces;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;

namespace ATM.Tests.Integration.Api;

public sealed class AtmErrorHandlingTests
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

    private static async Task AssertProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedCode)
    {
        Assert.Equal(
            expectedStatus,
            response.StatusCode);

        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);

        var content =
            await response.Content.ReadAsStringAsync();

        using var document = JsonDocument.Parse(content);

        var root = document.RootElement;

        Assert.Equal(
            (int)expectedStatus,
            root.GetProperty("status").GetInt32());

        Assert.Equal(
            expectedCode,
            root.GetProperty("code").GetString());

        Assert.False(
            string.IsNullOrWhiteSpace(
                root.GetProperty("traceId").GetString()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    [InlineData(0.001)]
    [InlineData(1000000.01)]
    public async Task Deposit_InvalidAmount_Returns400(
        double invalidAmount)
    {
        await using var factory = new AtmApiFactory();
        using var client = CreateClient(factory);

        var accounts = await GetAccountsAsync(client);

        var response = await client.PostWithIdempotencyKeyAsync(
            $"/api/accounts/{accounts[0].Id}/deposits",
            new { Amount = (decimal)invalidAmount });

        await AssertProblemAsync(
            response,
            HttpStatusCode.BadRequest,
            ErrorCodes.InvalidAmount);
    }

    [Fact]
    public async Task GetAccount_UnknownAccount_Returns404()
    {
        await using var factory = new AtmApiFactory();
        using var client = CreateClient(factory);

        var response = await client.GetAsync(
            $"/api/accounts/{Guid.NewGuid()}");

        await AssertProblemAsync(
            response,
            HttpStatusCode.NotFound,
            ErrorCodes.AccountNotFound);
    }

    [Fact]
    public async Task Withdraw_InsufficientFunds_Returns409()
    {
        await using var factory = new AtmApiFactory();
        using var client = CreateClient(factory);

        var accounts = await GetAccountsAsync(client);

        var response = await client.PostWithIdempotencyKeyAsync(
            $"/api/accounts/{accounts[0].Id}/withdrawals",
            new { Amount = 1500m });

        await AssertProblemAsync(
            response,
            HttpStatusCode.Conflict,
            ErrorCodes.InsufficientFunds);
    }

    [Fact]
    public async Task Transfer_SameAccount_Returns400()
    {
        await using var factory = new AtmApiFactory();
        using var client = CreateClient(factory);

        var accounts = await GetAccountsAsync(client);

        var response = await client.PostWithIdempotencyKeyAsync(
            "/api/transfers",
            new
            {
                SourceAccountId = accounts[0].Id,
                DestinationAccountId = accounts[0].Id,
                Amount = 100m
            });

        await AssertProblemAsync(
            response,
            HttpStatusCode.BadRequest,
            ErrorCodes.SameAccountTransfer);
    }

    [Fact]
    public async Task Transfer_UnknownDestination_Returns404()
    {
        await using var factory = new AtmApiFactory();
        using var client = CreateClient(factory);

        var accounts = await GetAccountsAsync(client);

        var response = await client.PostWithIdempotencyKeyAsync(
            "/api/transfers",
            new
            {
                SourceAccountId = accounts[0].Id,
                DestinationAccountId = Guid.NewGuid(),
                Amount = 100m
            });

        await AssertProblemAsync(
            response,
            HttpStatusCode.NotFound,
            ErrorCodes.AccountNotFound);
    }

    [Fact]
    public async Task Transfer_FailedOperation_PreservesBalances()
    {
        await using var factory = new AtmApiFactory();
        using var client = CreateClient(factory);

        var accounts = await GetAccountsAsync(client);

        var response = await client.PostWithIdempotencyKeyAsync(
            "/api/transfers",
            new
            {
                SourceAccountId = accounts[0].Id,
                DestinationAccountId = accounts[1].Id,
                Amount = 1500m
            });

        await AssertProblemAsync(
            response,
            HttpStatusCode.Conflict,
            ErrorCodes.InsufficientFunds);

        var updatedAccounts =
            await GetAccountsAsync(client);

        Assert.All(
            updatedAccounts,
            account => Assert.Equal(1000m, account.Balance));

        foreach (var account in updatedAccounts)
        {
            var history =
                await client.GetFromJsonAsync<TransactionDto[]>(
                    $"/api/accounts/{account.Id}/transactions");

            Assert.NotNull(history);

            // Only the seeded OpeningBalance entry may exist.
            Assert.DoesNotContain(
                history,
                t => t.Type != "OpeningBalance");
        }
    }

    [Fact]
    public async Task UnknownRoute_Returns404ProblemDetails()
    {
        await using var factory = new AtmApiFactory();
        using var client = CreateClient(factory);

        var response = await client.GetAsync(
            "/api/unknown");

        await AssertProblemAsync(
            response,
            HttpStatusCode.NotFound,
            ErrorCodes.NotFound);
    }

    [Fact]
    public async Task UnexpectedException_Returns500WithoutDetails()
    {
        await using var factory = new AtmApiFactory();

        var service = new Mock<IAtmService>();

        service.Setup(x => x.GetAccountsAsync(
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(
                new InvalidOperationException(
                    "Sensitive internal database information."));

        await using var customFactory =
            factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IAtmService>();

                    services.AddScoped<IAtmService>(
                        _ => service.Object);
                });
            });

        using var client = customFactory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost")
            });

        var response = await client.GetAsync("/api/accounts");

        await AssertProblemAsync(
            response,
            HttpStatusCode.InternalServerError,
            ErrorCodes.InternalServerError);

        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain(
            "Sensitive internal database information",
            body);

        Assert.DoesNotContain(
            "InvalidOperationException",
            body);
    }

    [Fact]
    public async Task DatabaseBusy_Returns503()
    {
        await using var factory = new AtmApiFactory();

        var service = new Mock<IAtmService>();

        service.Setup(x => x.GetAccountsAsync(
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(
                new SqliteException("Database busy.", 5));

        await using var customFactory =
            factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IAtmService>();

                    services.AddScoped<IAtmService>(
                        _ => service.Object);
                });
            });

        using var client = customFactory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost")
            });

        var response = await client.GetAsync("/api/accounts");

        await AssertProblemAsync(
            response,
            HttpStatusCode.ServiceUnavailable,
            ErrorCodes.DatabaseBusy);
    }
}