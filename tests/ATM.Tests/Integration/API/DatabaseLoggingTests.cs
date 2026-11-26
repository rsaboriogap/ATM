using System.Net.Http.Json;
using ATM.Api.ErrorHandling;
using ATM.Infrastructure.Logging;
using ATM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ATM.Tests.Integration.Api;

public sealed class DatabaseLoggingTests : BackendTestBase
{
    [Fact]
    public async Task RejectedRequest_IsPersistedToLogsTable()
    {
        await using var factory = new AtmApiFactory();

        using var client = CreateClient(factory);

        var accounts = await GetAccountsAsync(client);

        using var response = await client.PostAsJsonAsync(
            $"/api/accounts/{accounts[0].Id}/withdrawals",
            new { Amount = 5000m });

        Assert.False(response.IsSuccessStatusCode);

        var entry = await WaitForLogAsync(
            factory,
            typeof(GlobalExceptionHandler).FullName!);

        Assert.NotNull(entry);
        Assert.Equal("Information", entry.Level);
        Assert.StartsWith("ATM request rejected.", entry.Message);
        Assert.Contains(ErrorCodes.InsufficientFunds, entry.Message);
        Assert.Contains("\"ErrorCode\"", entry.Properties);
        Assert.False(string.IsNullOrWhiteSpace(entry.TraceId));
        Assert.Equal(DateTimeKind.Utc, entry.TimestampUtc.Kind);
    }

    [Fact]
    public async Task EntityFrameworkLogs_AreNotPersisted()
    {
        await using var factory = new AtmApiFactory();

        using var client = CreateClient(factory);

        await DepositAsync(
            client, (await GetAccountsAsync(client))[0].Id, 10m);

        using var scope = factory.Services.CreateScope();

        var context = scope.ServiceProvider
            .GetRequiredService<AtmDbContext>();

        Assert.False(await context.Logs.AnyAsync(
            log => log.Category.StartsWith(
                "Microsoft.EntityFrameworkCore")));
    }

    private static async Task<LogEntry?> WaitForLogAsync(
        AtmApiFactory factory,
        string category)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            using var scope = factory.Services.CreateScope();

            var context = scope.ServiceProvider
                .GetRequiredService<AtmDbContext>();

            var entry = await context.Logs
                .AsNoTracking()
                .FirstOrDefaultAsync(log => log.Category == category);

            if (entry is not null)
            {
                return entry;
            }

            await Task.Delay(100);
        }

        return null;
    }
}
