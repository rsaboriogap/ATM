using ATM.Application.DTOs;
using ATM.Application.Interfaces;
using ATM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ATM.Tests.Integration.ACID;

public sealed class DurabilityTests : AcidTestBase
{
    [Fact]
    public async Task CommittedTransaction_ShouldSurviveProviderRecreation()
    {
        await Run(s => s.TransferAsync(new TransferRequest(Checking, Savings, 250m)));
        await Provider.DisposeAsync();
        Provider = CreateProvider();
        await using var scope = Provider.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IAtmService>();
        Assert.Equal(750m, (await service.GetAccountAsync(Checking)).Balance);
        Assert.Equal(1250m, (await service.GetAccountAsync(Savings)).Balance);
        var state = await ReadState();
        Assert.Equal(4, state.Entries.Count);
        AssertReconciled(state);
    }

    [Fact]
    public async Task Migrations_ShouldInstallLedgerTriggers()
    {
        await using var scope = Provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtmDbContext>();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type='trigger' AND tbl_name='Transactions'";
        await using var reader = await command.ExecuteReaderAsync();
        var names = new List<string>();
        while (await reader.ReadAsync()) names.Add(reader.GetString(0));
        Assert.Contains("TR_Transactions_PreventUpdate", names);
        Assert.Contains("TR_Transactions_PreventDelete", names);
        Assert.Contains("TR_Transactions_PreventReplace", names);
    }

    [Fact]
    public async Task Connection_ShouldUseSynchronousFull_AndForeignKeys()
    {
        await using var scope = Provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtmDbContext>();
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "PRAGMA synchronous";
        Assert.Equal(2, Convert.ToInt32(await command.ExecuteScalarAsync()));
        command.CommandText = "PRAGMA foreign_keys";
        Assert.Equal(1, Convert.ToInt32(await command.ExecuteScalarAsync()));
        command.CommandText = "PRAGMA journal_mode";
        Assert.Equal("wal", Convert.ToString(await command.ExecuteScalarAsync())?.ToLowerInvariant());
    }
}
