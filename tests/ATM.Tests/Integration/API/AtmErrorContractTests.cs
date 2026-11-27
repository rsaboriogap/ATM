using System.Net;
using System.Net.Http.Json;
using System.Text;
using ATM.Api.ErrorHandling;
using Xunit;

namespace ATM.Tests.Integration.Api;

public sealed class AtmErrorContractTests : BackendTestBase
{
    [Fact]
    public async Task MissingAmount_ReturnsValidationProblem()
    {
        await using var factory = new AtmApiFactory();
        using var client = CreateClient(factory);

        var accounts = await GetAccountsAsync(client);

        using var response = await client.PostWithIdempotencyKeyAsync(
            $"/api/accounts/{accounts[0].Id}/deposits",
            new { });

        await AssertProblemAsync(
            response,
            HttpStatusCode.BadRequest,
            ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task MalformedJson_ReturnsProblemDetails()
    {
        await using var factory = new AtmApiFactory();
        using var client = CreateClient(factory);

        var accounts = await GetAccountsAsync(client);

        using var content = new StringContent(
            "{\"amount\":",
            Encoding.UTF8,
            "application/json");

        using var response = await client.PostAsync(
            $"/api/accounts/{accounts[0].Id}/deposits",
            content);

        await AssertProblemAsync(
            response,
            HttpStatusCode.BadRequest,
            ErrorCodes.InvalidRequest);
    }

    [Fact]
    public async Task UnsupportedMediaType_ReturnsProblemDetails()
    {
        await using var factory = new AtmApiFactory();
        using var client = CreateClient(factory);

        var accounts = await GetAccountsAsync(client);

        using var content = new StringContent(
            "150",
            Encoding.UTF8,
            "text/plain");

        using var response = await client.PostAsync(
            $"/api/accounts/{accounts[0].Id}/deposits",
            content);

        await AssertProblemAsync(
            response,
            HttpStatusCode.UnsupportedMediaType,
            ErrorCodes.UnsupportedMediaType);
    }

    [Fact]
    public async Task BusinessError_ClientRejectingJson_StillGetsStatusCode()
    {
        await using var factory = new AtmApiFactory();
        using var client = CreateClient(factory);

        var accounts = await GetAccountsAsync(client);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/accounts/{accounts[0].Id}/withdrawals")
        {
            Content = JsonContent.Create(new { Amount = 5000m })
        };
        request.Headers.Accept.ParseAdd("text/html");
        request.Headers.Add(
            IdempotencyTestExtensions.IdempotencyKeyHeader,
            Guid.NewGuid().ToString());

        using var response = await client.SendAsync(request);

        // No problem+json body (the client refused JSON), but the outcome is kept.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.NotEqual(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task RejectedRequests_DoNotModifyFinancialState()
    {
        await using var factory = new AtmApiFactory();
        using var client = CreateClient(factory);

        var accounts = await GetAccountsAsync(client);
        var account = accounts[0];

        using var missingAmount =
            await client.PostWithIdempotencyKeyAsync(
                $"/api/accounts/{account.Id}/deposits",
                new { });

        using var invalidAmount =
            await client.PostWithIdempotencyKeyAsync(
                $"/api/accounts/{account.Id}/deposits",
                new { Amount = -100m });

        using var insufficientFunds =
            await client.PostWithIdempotencyKeyAsync(
                $"/api/accounts/{account.Id}/withdrawals",
                new { Amount = 1500m });

        Assert.Equal(
            HttpStatusCode.BadRequest,
            missingAmount.StatusCode);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            invalidAmount.StatusCode);

        Assert.Equal(
            HttpStatusCode.Conflict,
            insufficientFunds.StatusCode);

        var persisted = await GetAccountAsync(
            client, account.Id);

        Assert.Equal(1000m, persisted.Balance);

        Assert.Empty(
            await GetHistoryAsync(client, account.Id));
    }
}