using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ATM.Client.DTOs;
using ATM.Client.Exceptions;
using ATM.Client.Interfaces;
using ATM.Client.Models;
using Microsoft.Extensions.Logging;

namespace ATM.Client.Services;

public sealed class AtmApiClient : IAtmApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AtmApiClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public AtmApiClient(
        HttpClient httpClient,
        ILogger<AtmApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<AccountDto>> GetAccountsAsync(
        CancellationToken cancellationToken = default)
    {
        return await SendAsync<AccountDto[]>(
            HttpMethod.Get,
            "api/accounts",
            cancellationToken: cancellationToken);
    }

    public Task<AccountDto> GetAccountAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<AccountDto>(
            HttpMethod.Get,
            $"api/accounts/{accountId}",
            cancellationToken: cancellationToken);
    }

    public Task<OperationResultDto> DepositAsync(
        Guid accountId,
        DepositRequest request,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<OperationResultDto>(
            HttpMethod.Post,
            $"api/accounts/{accountId}/deposits",
            request,
            cancellationToken);
    }

    public Task<OperationResultDto> WithdrawAsync(
        Guid accountId,
        WithdrawalRequest request,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<OperationResultDto>(
            HttpMethod.Post,
            $"api/accounts/{accountId}/withdrawals",
            request,
            cancellationToken);
    }

    public Task<TransferResultDto> TransferAsync(
        TransferRequest request,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<TransferResultDto>(
            HttpMethod.Post,
            "api/transfers",
            request,
            cancellationToken);
    }

    public async Task<IReadOnlyList<TransactionDto>>
        GetTransactionHistoryAsync(
            Guid accountId,
            CancellationToken cancellationToken = default)
    {
        return await SendAsync<TransactionDto[]>(
            HttpMethod.Get,
            $"api/accounts/{accountId}/transactions",
            cancellationToken: cancellationToken);
    }

    private async Task<TResponse> SendAsync<TResponse>(
        HttpMethod method,
        string endpoint,
        object? body = null,
        CancellationToken cancellationToken = default)
        where TResponse : class
    {
        var isMutation = method != HttpMethod.Get;

        cancellationToken.ThrowIfCancellationRequested();

        using var request = new HttpRequestMessage(
            method,
            endpoint);

        if (body is not null)
        {
            request.Content = JsonContent.Create(
                body,
                options: JsonOptions);
        }

        try
        {
            using var response = await _httpClient.SendAsync(
                request,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw await CreateApiExceptionAsync(
                    response,
                    isMutation,
                    cancellationToken);
            }

            var result = await response.Content
                .ReadFromJsonAsync<TResponse>(
                    JsonOptions,
                    cancellationToken);

            if (result is null)
            {
                throw new ApiException(
                    "The API returned an empty response.",
                    "invalid_response",
                    response.StatusCode,
                    isMutationOutcomeUnknown: isMutation);
            }

            return result;
        }
        catch (OperationCanceledException exception)
            when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "API request timed out: {Method} {Endpoint}",
                method,
                endpoint);

            throw new ApiException(
                "The request timed out.",
                "request_timeout",
                innerException: exception,
                isMutationOutcomeUnknown: isMutation);
        }
        catch (OperationCanceledException exception)
            when (isMutation)
        {
            throw new ApiException(
                "The financial request was interrupted.",
                "request_cancelled",
                innerException: exception,
                isMutationOutcomeUnknown: true);
        }
        catch (HttpRequestException exception)
        {
            _logger.LogWarning(
                "API connection failed: {Method} {Endpoint}",
                method,
                endpoint);

            throw new ApiException(
                "Unable to communicate with the ATM server.",
                "network_error",
                innerException: exception,
                isMutationOutcomeUnknown: isMutation);
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(
                "Invalid API response: {Method} {Endpoint}",
                method,
                endpoint);

            throw new ApiException(
                "The server returned an invalid response.",
                "invalid_response",
                innerException: exception,
                isMutationOutcomeUnknown: isMutation);
        }
        catch (NotSupportedException exception)
        {
            throw new ApiException(
                "The API returned an unsupported response format.",
                "invalid_response",
                innerException: exception,
                isMutationOutcomeUnknown: isMutation);
        }
    }

    private async Task<ApiException> CreateApiExceptionAsync(
        HttpResponseMessage response,
        bool isMutation,
        CancellationToken cancellationToken)
    {
        ApiProblemDetails? problem = null;

        try
        {
            problem = await response.Content
                .ReadFromJsonAsync<ApiProblemDetails>(
                    JsonOptions,
                    cancellationToken);
        }
        catch (JsonException)
        {
            // The server may have returned HTML or plain text.
        }
        catch (NotSupportedException)
        {
            // The response format is not supported.
        }

        var code = problem?.Code
            ?? $"http_{(int)response.StatusCode}";

        var isServerError = (int)response.StatusCode >= 500;

        var message = isServerError
            ? "The ATM server could not complete the request."
            : problem?.Detail
                ?? problem?.Title
                ?? GetDefaultErrorMessage(response.StatusCode);

        _logger.LogWarning(
            "API request failed. Status: {StatusCode}, " +
            "Code: {Code}, TraceId: {TraceId}",
            (int)response.StatusCode,
            code,
            problem?.TraceId);

        return new ApiException(
            message,
            code,
            response.StatusCode,
            problem,
            isMutationOutcomeUnknown:
                isMutation && isServerError);
    }

    private static string GetDefaultErrorMessage(
        HttpStatusCode statusCode)
    {
        return statusCode switch
        {
            HttpStatusCode.BadRequest =>
                "The request contains invalid information.",

            HttpStatusCode.NotFound =>
                "The requested resource was not found.",

            HttpStatusCode.Conflict =>
                "The operation could not be completed.",

            HttpStatusCode.UnsupportedMediaType =>
                "The request format is not supported.",

            HttpStatusCode.ServiceUnavailable =>
                "The ATM server is temporarily unavailable.",

            _ => "An unexpected API error occurred."
        };
    }
}