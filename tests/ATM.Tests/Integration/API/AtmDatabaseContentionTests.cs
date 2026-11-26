using System.Net;
using System.Net.Http.Json;
using ATM.Api.ErrorHandling;
using Microsoft.Data.Sqlite;
using Xunit;

namespace ATM.Tests.Integration.Api;

public sealed class AtmDatabaseContentionTests
    : BackendTestBase
{
    [Fact]
    public async Task LockedDatabase_Returns503_WithoutChanges()
    {
        await using var factory = new AtmApiFactory(
            defaultTimeout: 1);

        using var client = CreateClient(factory);

        var accounts = await GetAccountsAsync(client);
        var account = accounts[0];

        var connectionString =
            new SqliteConnectionStringBuilder
            {
                DataSource = factory.DatabasePath,
                Pooling = false,
                DefaultTimeout = 1
            }.ToString();

        await using (var connection =
            new SqliteConnection(connectionString))
        {
            await connection.OpenAsync();

            await using var blockingTransaction =
                connection.BeginTransaction(deferred: false);

            try
            {
                using var response =
                    await client.PostAsJsonAsync(
                        $"/api/accounts/{account.Id}/deposits",
                        new { Amount = 100m });

                await AssertProblemAsync(
                    response,
                    HttpStatusCode.ServiceUnavailable,
                    ErrorCodes.DatabaseBusy);
            }
            finally
            {
                await blockingTransaction.RollbackAsync();
            }
        }

        var persisted = await GetAccountAsync(
            client, account.Id);

        Assert.Equal(1000m, persisted.Balance);

        Assert.Empty(
            await GetHistoryAsync(client, account.Id));

        // A new operation should work after releasing the lock.
        var result = await DepositAsync(
            client, account.Id, 100m);

        Assert.Equal(1100m, result.Account.Balance);
    }
}