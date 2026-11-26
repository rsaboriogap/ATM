using Xunit;

namespace ATM.Tests.Integration.Api;

public sealed class AtmPersistenceTests : BackendTestBase
{
    [Fact]
    public async Task ApplicationRestart_PreservesFinancialData()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"atm-restart-{Guid.NewGuid():N}.db");

        try
        {
            Guid checkingId;
            Guid savingsId;
            Guid referenceId;

            await using (var factory =
                new AtmApiFactory(databasePath))
            {
                using var client = CreateClient(factory);

                var accounts = await GetAccountsAsync(client);

                checkingId = accounts.Single(
                    a => a.Name == "Checking").Id;

                savingsId = accounts.Single(
                    a => a.Name == "Savings").Id;

                await DepositAsync(
                    client, checkingId, 500m);

                var transfer = await TransferAsync(
                    client,
                    checkingId,
                    savingsId,
                    300m);

                referenceId = transfer.ReferenceId;
            }

            Assert.True(File.Exists(databasePath));

            // Simulate a new application startup.
            await using (var factory =
                new AtmApiFactory(databasePath))
            {
                using var client = CreateClient(factory);

                var accounts = await GetAccountsAsync(client);

                Assert.Equal(2, accounts.Length);

                var checking = accounts.Single(
                    a => a.Id == checkingId);

                var savings = accounts.Single(
                    a => a.Id == savingsId);

                Assert.Equal(1200m, checking.Balance);
                Assert.Equal(1300m, savings.Balance);

                var checkingHistory = await GetHistoryAsync(
                    client, checkingId);

                var savingsHistory = await GetHistoryAsync(
                    client, savingsId);

                Assert.Equal(2, checkingHistory.Length);
                Assert.Single(savingsHistory);

                Assert.Contains(
                    checkingHistory,
                    t => t.ReferenceId == referenceId
                        && t.Type == "TransferOut");

                Assert.Contains(
                    savingsHistory,
                    t => t.ReferenceId == referenceId
                        && t.Type == "TransferIn");
            }
        }
        finally
        {
            AtmApiFactory.DeleteDatabaseFiles(databasePath);
        }
    }
}