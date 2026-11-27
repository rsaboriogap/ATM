using System.Net;
using System.Net.Http.Json;
using ATM.Api.ErrorHandling;
using ATM.Application.DTOs;
using Xunit;

namespace ATM.Tests.Integration.Api;

public sealed class IdempotencyApiTests : BackendTestBase
{
    public static TheoryData<string> WriteRoutes => new()
    {
        "deposits",
        "withdrawals"
    };

    [Theory]
    [MemberData(nameof(WriteRoutes))]
    public async Task MissingHeader_Returns400AndDoesNotPost(string route)
    {
        await using var factory = new AtmApiFactory();
        using var client = CreateClient(factory);

        var account = (await GetAccountsAsync(client))[0];

        using var response = await client.PostAsJsonAsync(
            $"/api/accounts/{account.Id}/{route}",
            new { Amount = 100m });

        await AssertProblemAsync(
            response,
            HttpStatusCode.BadRequest,
            ErrorCodes.IdempotencyKeyRequired);

        Assert.Empty(await GetHistoryAsync(client, account.Id));
    }

    [Fact]
    public async Task Transfer_MissingHeader_Returns400()
    {
        await using var factory = new AtmApiFactory();
        using var client = CreateClient(factory);

        var accounts = await GetAccountsAsync(client);

        using var response = await client.PostAsJsonAsync(
            "/api/transfers",
            new
            {
                SourceAccountId = accounts[0].Id,
                DestinationAccountId = accounts[1].Id,
                Amount = 100m
            });

        await AssertProblemAsync(
            response,
            HttpStatusCode.BadRequest,
            ErrorCodes.IdempotencyKeyRequired);
    }

    [Fact]
    public async Task EmptyGuidHeader_Returns400()
    {
        await using var factory = new AtmApiFactory();
        using var client = CreateClient(factory);

        var account = (await GetAccountsAsync(client))[0];

        using var response = await client.PostWithIdempotencyKeyAsync(
            $"/api/accounts/{account.Id}/deposits",
            new { Amount = 100m },
            Guid.Empty);

        await AssertProblemAsync(
            response,
            HttpStatusCode.BadRequest,
            ErrorCodes.IdempotencyKeyRequired);
    }

    [Fact]
    public async Task MalformedHeader_Returns400()
    {
        await using var factory = new AtmApiFactory();
        using var client = CreateClient(factory);

        var account = (await GetAccountsAsync(client))[0];

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/accounts/{account.Id}/deposits")
        {
            Content = JsonContent.Create(new { Amount = 100m })
        };

        request.Headers.Add(
            IdempotencyTestExtensions.IdempotencyKeyHeader,
            "not-a-guid");

        using var response = await client.SendAsync(request);

        await AssertProblemAsync(
            response,
            HttpStatusCode.BadRequest,
            ErrorCodes.InvalidRequest);

        Assert.Empty(await GetHistoryAsync(client, account.Id));
    }

    [Fact]
    public async Task Withdrawal_Replay_ReturnsSameBodyAndPostsOnce()
    {
        await using var factory = new AtmApiFactory();
        using var client = CreateClient(factory);

        var account = (await GetAccountsAsync(client))[0];
        var key = Guid.NewGuid();
        var url = $"/api/accounts/{account.Id}/withdrawals";

        using var first = await client.PostWithIdempotencyKeyAsync(
            url, new { Amount = 100m }, key);
        using var second = await client.PostWithIdempotencyKeyAsync(
            url, new { Amount = 100m }, key);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        Assert.Equal(
            await first.Content.ReadFromJsonAsync<OperationResultDto>(),
            await second.Content.ReadFromJsonAsync<OperationResultDto>());

        Assert.Equal(900m, (await GetAccountAsync(client, account.Id)).Balance);
        Assert.Single(await GetHistoryAsync(client, account.Id));
    }

    [Fact]
    public async Task Transfer_Replay_PostsOnePair()
    {
        await using var factory = new AtmApiFactory();
        using var client = CreateClient(factory);

        var accounts = await GetAccountsAsync(client);
        var key = Guid.NewGuid();
        var body = new
        {
            SourceAccountId = accounts[0].Id,
            DestinationAccountId = accounts[1].Id,
            Amount = 300m
        };

        using var first = await client.PostWithIdempotencyKeyAsync(
            "/api/transfers", body, key);
        using var second = await client.PostWithIdempotencyKeyAsync(
            "/api/transfers", body, key);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        Assert.Equal(
            (await first.Content.ReadFromJsonAsync<TransferResultDto>())!.ReferenceId,
            (await second.Content.ReadFromJsonAsync<TransferResultDto>())!.ReferenceId);

        Assert.Single(await GetHistoryAsync(client, accounts[0].Id));
        Assert.Single(await GetHistoryAsync(client, accounts[1].Id));
    }

    [Fact]
    public async Task SameKey_DifferentBody_Returns422()
    {
        await using var factory = new AtmApiFactory();
        using var client = CreateClient(factory);

        var account = (await GetAccountsAsync(client))[0];
        var key = Guid.NewGuid();
        var url = $"/api/accounts/{account.Id}/deposits";

        using var first = await client.PostWithIdempotencyKeyAsync(
            url, new { Amount = 100m }, key);
        using var conflict = await client.PostWithIdempotencyKeyAsync(
            url, new { Amount = 200m }, key);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        await AssertProblemAsync(
            conflict,
            (HttpStatusCode)422,
            ErrorCodes.IdempotencyKeyConflict);

        Assert.Equal(1100m, (await GetAccountAsync(client, account.Id)).Balance);
    }

    [Fact]
    public async Task ConcurrentRequests_SameKey_PostOnce()
    {
        await using var factory = new AtmApiFactory(defaultTimeout: 15);
        using var client = CreateClient(factory);

        var account = (await GetAccountsAsync(client))[0];
        var key = Guid.NewGuid();

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 5).Select(_ =>
                client.PostWithIdempotencyKeyAsync(
                    $"/api/accounts/{account.Id}/deposits",
                    new { Amount = 50m },
                    key)));

        try
        {
            Assert.All(responses, r =>
                Assert.Equal(HttpStatusCode.OK, r.StatusCode));

            Assert.Equal(1050m, (await GetAccountAsync(client, account.Id)).Balance);
            Assert.Single(await GetHistoryAsync(client, account.Id));
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }
}
