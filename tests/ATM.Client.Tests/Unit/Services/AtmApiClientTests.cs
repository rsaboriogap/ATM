using ATM.Client.DTOs;
using ATM.Client.Exceptions;
using ATM.Client.Services;
using ATM.Client.Tests.Unit.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Xunit;
using Xunit.Sdk;

namespace ATM.Client.Tests.Unit.Services;

public sealed class AtmApiClientTests
{
    [Fact]
    public async Task GetAccounts_ReturnsAccounts()
    {
        var accounts = new[]
        {
            CreateAccount("Checking"),
            CreateAccount("Savings")
        };

        using var httpClient = CreateHttpClient(
            (request, ct) =>
            {
                Assert.Equal(HttpMethod.Get, request.Method);

                Assert.Equal(
                    "/api/accounts",
                    request.RequestUri!.AbsolutePath);

                return Task.FromResult(
                    JsonResponse(HttpStatusCode.OK, accounts));
            });

        var client = CreateClient(httpClient);

        var result = await client.GetAccountsAsync();

        Assert.Equal(2, result.Count);
        Assert.All(result, a => Assert.Equal(1000m, a.Balance));
    }

    [Fact]
    public async Task GetAccount_UsesCorrectEndpoint()
    {
        var account = CreateAccount("Checking");

        using var httpClient = CreateHttpClient(
            (request, ct) =>
            {
                Assert.Equal(HttpMethod.Get, request.Method);

                Assert.Equal(
                    $"/api/accounts/{account.Id}",
                    request.RequestUri!.AbsolutePath);

                return Task.FromResult(
                    JsonResponse(HttpStatusCode.OK, account));
            });

        var client = CreateClient(httpClient);

        var result = await client.GetAccountAsync(account.Id);

        Assert.Equal(account, result);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AccountOperation_SendsCorrectRequest(
        bool isDeposit)
    {
        var account = CreateAccount("Checking");

        var operation = isDeposit
            ? "deposits"
            : "withdrawals";

        var transactionType = isDeposit
            ? "Deposit"
            : "Withdrawal";

        var balance = isDeposit
            ? 1150m
            : 850m;

        var expected = new OperationResultDto(
            account with { Balance = balance },
            CreateTransaction(
                account.Id,
                transactionType,
                150m,
                balance));

        using var httpClient = CreateHttpClient(
            async (request, ct) =>
            {
                Assert.Equal(HttpMethod.Post, request.Method);

                Assert.Equal(
                    $"/api/accounts/{account.Id}/{operation}",
                    request.RequestUri!.AbsolutePath);

                Assert.Equal(
                    "application/json",
                    request.Content!.Headers.ContentType?.MediaType);

                var body = await request.Content
                    .ReadFromJsonAsync<DepositRequest>(
                        cancellationToken: ct);

                Assert.NotNull(body);
                Assert.Equal(150m, body.Amount);

                return JsonResponse(
                    HttpStatusCode.OK,
                    expected);
            });

        var client = CreateClient(httpClient);

        var result = isDeposit
            ? await client.DepositAsync(
                account.Id,
                new DepositRequest(150m),
                Guid.NewGuid())
            : await client.WithdrawAsync(
                account.Id,
                new WithdrawalRequest(150m),
                Guid.NewGuid());

        Assert.Equal(balance, result.Account.Balance);
        Assert.Equal(transactionType, result.Transaction.Type);
    }

    [Fact]
    public async Task Transfer_SendsCorrectRequest()
    {
        var source = CreateAccount("Checking");
        var destination = CreateAccount("Savings");

        var referenceId = Guid.NewGuid();

        var expected = new TransferResultDto(
            referenceId,
            source with { Balance = 700m },
            destination with { Balance = 1300m },
            CreateTransaction(
                source.Id,
                "TransferOut",
                300m,
                700m,
                referenceId),
            CreateTransaction(
                destination.Id,
                "TransferIn",
                300m,
                1300m,
                referenceId));

        using var httpClient = CreateHttpClient(
            async (request, ct) =>
            {
                Assert.Equal(HttpMethod.Post, request.Method);

                Assert.Equal(
                    "/api/transfers",
                    request.RequestUri!.AbsolutePath);

                var body = await request.Content!
                    .ReadFromJsonAsync<TransferRequest>(
                        cancellationToken: ct);

                Assert.NotNull(body);
                Assert.Equal(source.Id, body.SourceAccountId);
                Assert.Equal(destination.Id, body.DestinationAccountId);
                Assert.Equal(300m, body.Amount);

                return JsonResponse(
                    HttpStatusCode.OK,
                    expected);
            });

        var client = CreateClient(httpClient);

        var result = await client.TransferAsync(
            new TransferRequest(
                source.Id,
                destination.Id,
                300m),
            Guid.NewGuid());

        Assert.Equal(700m, result.SourceAccount.Balance);
        Assert.Equal(1300m, result.DestinationAccount.Balance);
        Assert.Equal(referenceId, result.ReferenceId);
    }

    [Fact]
    public async Task Deposit_SendsIdempotencyKeyHeader()
    {
        var account = CreateAccount("Checking");
        var key = Guid.NewGuid();
        string? sentKey = null;

        using var httpClient = CreateHttpClient(
            (request, ct) =>
            {
                sentKey = request.Headers
                    .GetValues("Idempotency-Key")
                    .Single();

                return Task.FromResult(JsonResponse(
                    HttpStatusCode.OK,
                    new OperationResultDto(
                        account,
                        CreateTransaction(
                            account.Id,
                            "Deposit",
                            100m,
                            1100m))));
            });

        var client = CreateClient(httpClient);

        await client.DepositAsync(
            account.Id,
            new DepositRequest(100m),
            key);

        Assert.Equal(key.ToString(), sentKey);
    }

    [Fact]
    public async Task GetAccounts_DoesNotSendIdempotencyKey()
    {
        var hasKey = true;

        using var httpClient = CreateHttpClient(
            (request, ct) =>
            {
                hasKey = request.Headers.Contains("Idempotency-Key");

                return Task.FromResult(JsonResponse(
                    HttpStatusCode.OK,
                    Array.Empty<AccountDto>()));
            });

        var client = CreateClient(httpClient);

        await client.GetAccountsAsync();

        Assert.False(hasKey);
    }

    [Fact]
    public async Task GetHistory_ReturnsTransactions()
    {
        var account = CreateAccount("Checking");

        var transaction = CreateTransaction(
            account.Id,
            "Deposit",
            100m,
            1100m);

        using var httpClient = CreateHttpClient(
            (request, ct) =>
            {
                Assert.Equal(HttpMethod.Get, request.Method);

                Assert.Equal(
                    $"/api/accounts/{account.Id}/transactions",
                    request.RequestUri!.AbsolutePath);

                return Task.FromResult(
                    JsonResponse(
                        HttpStatusCode.OK,
                        new[] { transaction }));
            });

        var client = CreateClient(httpClient);

        var result = await client.GetTransactionHistoryAsync(
            account.Id);

        Assert.Equal(transaction, Assert.Single(result));
    }

    [Theory]
    [InlineData(400, "validation_error")]
    [InlineData(404, "account_not_found")]
    [InlineData(409, "insufficient_funds")]
    [InlineData(409, "balance_limit_exceeded")]
    [InlineData(415, "unsupported_media_type")]
    [InlineData(503, "database_busy")]
    public async Task ApiError_PreservesProblemDetails(
        int status,
        string code)
    {
        using var httpClient = CreateHttpClient(
            (request, ct) =>
            {
                var response = JsonResponse(
                    (HttpStatusCode)status,
                    new
                    {
                        Title = "Operation failed",
                        Status = status,
                        Code = code,
                        Detail = "Test error message.",
                        TraceId = "test-trace-id"
                    });

                return Task.FromResult(response);
            });

        var client = CreateClient(httpClient);

        var exception = await Assert.ThrowsAsync<ApiException>(
            () => client.DepositAsync(
                Guid.NewGuid(),
                new DepositRequest(100m),
                Guid.NewGuid()));

        Assert.Equal(code, exception.Code);

        Assert.Equal(
            (HttpStatusCode)status,
            exception.StatusCode);

        Assert.Equal(
            "test-trace-id",
            exception.TraceId);

        Assert.Equal(
            status >= 500,
            exception.IsMutationOutcomeUnknown);
    }

    [Fact]
    public async Task ValidationError_PreservesFieldErrors()
    {
        using var httpClient = CreateHttpClient(
            (request, ct) =>
            {
                return Task.FromResult(
                    JsonResponse(
                        HttpStatusCode.BadRequest,
                        new
                        {
                            Code = "validation_error",
                            Status = 400,
                            Errors = new Dictionary<string, string[]>
                            {
                                ["Amount"] =
                                    new[] { "Invalid amount." }
                            }
                        }));
            });

        var client = CreateClient(httpClient);

        var exception = await Assert.ThrowsAsync<ApiException>(
            () => client.DepositAsync(
                Guid.NewGuid(),
                new DepositRequest(0m),
                Guid.NewGuid()));

        Assert.NotNull(exception.ProblemDetails?.Errors);

        Assert.Contains(
            "Amount",
            exception.ProblemDetails.Errors.Keys);
    }

    [Fact]
    public async Task NetworkFailure_ReturnsApiException()
    {
        using var httpClient = CreateHttpClient(
            (request, ct) =>
                throw new HttpRequestException(
                    "Connection failed."));

        var client = CreateClient(httpClient);

        var exception = await Assert.ThrowsAsync<ApiException>(
            () => client.GetAccountsAsync());

        Assert.Equal("network_error", exception.Code);
        Assert.False(exception.IsMutationOutcomeUnknown);
    }

    [Fact]
    public async Task MutationNetworkFailure_IsNotRetried()
    {
        var requestCount = 0;

        using var httpClient = CreateHttpClient(
            (request, ct) =>
            {
                requestCount++;

                throw new HttpRequestException(
                    "Connection interrupted.");
            });

        var client = CreateClient(httpClient);

        var exception = await Assert.ThrowsAsync<ApiException>(
            () => client.DepositAsync(
                Guid.NewGuid(),
                new DepositRequest(100m),
                Guid.NewGuid()));

        Assert.True(exception.IsMutationOutcomeUnknown);
        Assert.Equal(1, requestCount);
    }

    [Fact]
    public async Task InvalidJson_ReturnsInvalidResponseError()
    {
        using var httpClient = CreateHttpClient(
            (request, ct) =>
                Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            "{invalid-json",
                            Encoding.UTF8,
                            "application/json")
                    }));

        var client = CreateClient(httpClient);

        var exception = await Assert.ThrowsAsync<ApiException>(
            () => client.GetAccountsAsync());

        Assert.Equal("invalid_response", exception.Code);
    }

    [Fact]
    public async Task NonJsonServerError_UsesFallbackMessage()
    {
        using var httpClient = CreateHttpClient(
            (request, ct) =>
                Task.FromResult(
                    new HttpResponseMessage(
                        HttpStatusCode.BadGateway)
                    {
                        Content = new StringContent(
                            "<html>Bad Gateway</html>",
                            Encoding.UTF8,
                            "text/html")
                    }));

        var client = CreateClient(httpClient);

        var exception = await Assert.ThrowsAsync<ApiException>(
            () => client.GetAccountsAsync());

        Assert.Equal("http_502", exception.Code);

        Assert.Equal(
            HttpStatusCode.BadGateway,
            exception.StatusCode);
    }

    [Fact]
    public async Task RequestTimeout_ReturnsTimeoutError()
    {
        using var httpClient = CreateHttpClient(
            async (request, ct) =>
            {
                await Task.Delay(
                    Timeout.InfiniteTimeSpan,
                    ct);

                return JsonResponse(
                    HttpStatusCode.OK,
                    Array.Empty<AccountDto>());
            });

        httpClient.Timeout = TimeSpan.FromMilliseconds(100);

        var client = CreateClient(httpClient);

        var exception = await Assert.ThrowsAsync<ApiException>(
            () => client.GetAccountsAsync());

        Assert.Equal("request_timeout", exception.Code);
    }

    [Fact]
    public async Task CancelledRead_PropagatesCancellation()
    {
        using var httpClient = CreateHttpClient(
            async (request, ct) =>
            {
                await Task.Delay(
                    Timeout.InfiniteTimeSpan,
                    ct);

                return JsonResponse(
                    HttpStatusCode.OK,
                    Array.Empty<AccountDto>());
            });

        using var cancellation = new CancellationTokenSource();

        var client = CreateClient(httpClient);

        var operation = client.GetAccountsAsync(
            cancellation.Token);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await operation);
    }

    [Fact]
    public async Task CancelledMutation_HasUnknownOutcome()
    {
        using var httpClient = CreateHttpClient(
            async (request, ct) =>
            {
                await Task.Delay(
                    Timeout.InfiniteTimeSpan,
                    ct);

                return JsonResponse(
                    HttpStatusCode.OK,
                    new { });
            });

        using var cancellation = new CancellationTokenSource();

        var client = CreateClient(httpClient);

        var operation = client.DepositAsync(
            Guid.NewGuid(),
            new DepositRequest(100m),
            Guid.NewGuid(),
            cancellation.Token);

        cancellation.Cancel();

        var exception = await Assert.ThrowsAsync<ApiException>(
            async () => await operation);

        Assert.Equal("request_cancelled", exception.Code);
        Assert.True(exception.IsMutationOutcomeUnknown);
    }

    private static HttpClient CreateHttpClient(
        Func<
            HttpRequestMessage,
            CancellationToken,
            Task<HttpResponseMessage>> handler)
    {
        return new HttpClient(
            new StubHttpMessageHandler(handler))
        {
            BaseAddress = new Uri(
                "https://localhost:7102/")
        };
    }

    private static AtmApiClient CreateClient(
        HttpClient httpClient)
    {
        return new AtmApiClient(
            httpClient,
            NullLogger<AtmApiClient>.Instance);
    }

    private static HttpResponseMessage JsonResponse<T>(
        HttpStatusCode statusCode,
        T content)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = JsonContent.Create(content)
        };
    }

    private static AccountDto CreateAccount(string name)
    {
        return new AccountDto(
            Guid.NewGuid(),
            name,
            1000m,
            "USD",
            DateTimeOffset.UtcNow);
    }

    private static TransactionDto CreateTransaction(
        Guid accountId,
        string type,
        decimal amount,
        decimal balanceAfter,
        Guid? referenceId = null)
    {
        return new TransactionDto(
            Guid.NewGuid(),
            accountId,
            type,
            amount,
            balanceAfter,
            referenceId,
            DateTimeOffset.UtcNow);
    }
}