using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ATM.Application.DTOs;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace ATM.Tests.Integration.Api;

public abstract class BackendTestBase
{
    protected static HttpClient CreateClient(
        WebApplicationFactory<Program> factory)
    {
        return factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false
            });
    }

    protected static async Task<AccountDto[]> GetAccountsAsync(
        HttpClient client)
    {
        return await client.GetFromJsonAsync<AccountDto[]>(
            "/api/accounts")
            ?? throw new InvalidOperationException(
                "Accounts response was empty.");
    }

    protected static async Task<AccountDto> GetAccountAsync(
        HttpClient client,
        Guid accountId)
    {
        return await client.GetFromJsonAsync<AccountDto>(
            $"/api/accounts/{accountId}")
            ?? throw new InvalidOperationException(
                "Account response was empty.");
    }

    /// <summary>
    /// Returns customer-initiated transactions only. Seeded accounts carry an
    /// OpeningBalance ledger entry, which is excluded so tests can assert on
    /// the operations they perform. Use <see cref="GetLedgerAsync"/> for the
    /// full ledger.
    /// </summary>
    protected static async Task<TransactionDto[]> GetHistoryAsync(
        HttpClient client,
        Guid accountId)
    {
        var ledger = await GetLedgerAsync(client, accountId);

        return ledger
            .Where(t => t.Type != OpeningBalanceType)
            .ToArray();
    }

    protected static async Task<TransactionDto[]> GetLedgerAsync(
        HttpClient client,
        Guid accountId)
    {
        return await client.GetFromJsonAsync<TransactionDto[]>(
            $"/api/accounts/{accountId}/transactions")
            ?? throw new InvalidOperationException(
                "Transaction history was empty.");
    }

    protected const string OpeningBalanceType = "OpeningBalance";

    protected static async Task<OperationResultDto> DepositAsync(
        HttpClient client,
        Guid accountId,
        decimal amount)
    {
        using var response = await client.PostAsJsonAsync(
            $"/api/accounts/{accountId}/deposits",
            new { Amount = amount });

        response.EnsureSuccessStatusCode();

        return await response.Content
            .ReadFromJsonAsync<OperationResultDto>()
            ?? throw new InvalidOperationException(
                "Deposit response was empty.");
    }

    protected static async Task<OperationResultDto> WithdrawAsync(
        HttpClient client,
        Guid accountId,
        decimal amount)
    {
        using var response = await client.PostAsJsonAsync(
            $"/api/accounts/{accountId}/withdrawals",
            new { Amount = amount });

        response.EnsureSuccessStatusCode();

        return await response.Content
            .ReadFromJsonAsync<OperationResultDto>()
            ?? throw new InvalidOperationException(
                "Withdrawal response was empty.");
    }

    protected static async Task<TransferResultDto> TransferAsync(
        HttpClient client,
        Guid sourceAccountId,
        Guid destinationAccountId,
        decimal amount)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/transfers",
            new
            {
                SourceAccountId = sourceAccountId,
                DestinationAccountId = destinationAccountId,
                Amount = amount
            });

        response.EnsureSuccessStatusCode();

        return await response.Content
            .ReadFromJsonAsync<TransferResultDto>()
            ?? throw new InvalidOperationException(
                "Transfer response was empty.");
    }

    protected static async Task AssertProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedCode)
    {
        Assert.Equal(expectedStatus, response.StatusCode);

        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);

        var content = await response.Content.ReadAsStringAsync();

        using var document = JsonDocument.Parse(content);

        var root = document.RootElement;

        Assert.Equal(
            (int)expectedStatus,
            root.GetProperty("status").GetInt32());

        Assert.Equal(
            expectedCode,
            root.GetProperty("code").GetString());

        Assert.False(string.IsNullOrWhiteSpace(
            root.GetProperty("traceId").GetString()));
    }
}