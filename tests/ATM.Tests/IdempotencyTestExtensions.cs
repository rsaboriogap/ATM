using System.Net.Http.Json;
using ATM.Application.DTOs;
using ATM.Application.Interfaces;

namespace ATM.Tests;

/// <summary>
/// For tests that don't exercise idempotency: every call gets a new key, so
/// each one is a distinct operation. Idempotency tests pass keys explicitly.
/// </summary>
internal static class IdempotencyTestExtensions
{
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    public static Task<OperationResultDto> DepositAsync(
        this IAtmService service,
        Guid accountId,
        decimal amount) =>
        service.DepositAsync(accountId, amount, Guid.NewGuid());

    public static Task<OperationResultDto> WithdrawAsync(
        this IAtmService service,
        Guid accountId,
        decimal amount) =>
        service.WithdrawAsync(accountId, amount, Guid.NewGuid());

    public static Task<TransferResultDto> TransferAsync(
        this IAtmService service,
        TransferRequest request) =>
        service.TransferAsync(request, Guid.NewGuid());

    /// <summary>
    /// POSTs JSON with an Idempotency-Key header: a new key unless one is given.
    /// </summary>
    public static Task<HttpResponseMessage> PostWithIdempotencyKeyAsync<T>(
        this HttpClient client,
        string requestUri,
        T value,
        Guid? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = JsonContent.Create(value)
        };

        request.Headers.Add(
            IdempotencyKeyHeader,
            (idempotencyKey ?? Guid.NewGuid()).ToString());

        return client.SendAsync(request);
    }
}
