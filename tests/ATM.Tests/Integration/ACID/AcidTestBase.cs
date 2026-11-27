using ATM.Application;
using ATM.Application.Interfaces;
using ATM.Domain.Entities;
using ATM.Domain.Enums;
using ATM.Infrastructure;
using ATM.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ATM.Tests.Integration.ACID;

public abstract class AcidTestBase : IAsyncLifetime
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "atm-acid-" + Guid.NewGuid().ToString("N"));
    protected string ConnectionString { get; private set; } = null!;
    protected ServiceProvider Provider { get; set; } = null!;
    protected Guid Checking { get; private set; }
    protected Guid Savings { get; private set; }

    protected ServiceProvider CreateProvider()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConnectionStrings:AtmDatabase"] = ConnectionString }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddInfrastructure(configuration);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_directory);
        ConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(_directory, "atm.db"), ForeignKeys = true,
            Pooling = false, DefaultTimeout = 10
        }.ToString();
        Provider = CreateProvider();
        await using var scope = Provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync();
        var db = scope.ServiceProvider.GetRequiredService<AtmDbContext>();
        var accounts = await db.Accounts.AsNoTracking().ToListAsync();
        Checking = accounts.Single(a => a.Name == "Checking").Id;
        Savings = accounts.Single(a => a.Name == "Savings").Id;
    }

    public async Task DisposeAsync()
    {
        if (Provider is not null) await Provider.DisposeAsync();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    // Each operation uses its own scope; EfUnitOfWork requires a clean DbContext.
    protected async Task<T> Run<T>(Func<IAtmService, Task<T>> action)
    {
        await using var scope = Provider.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IAtmService>());
    }

    protected async Task<(List<Account> Accounts, List<Transaction> Entries)> ReadState()
    {
        await using var scope = Provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtmDbContext>();
        return (await db.Accounts.AsNoTracking().ToListAsync(), await db.Transactions.AsNoTracking().ToListAsync());
    }

    protected async Task<int> Sql(string sql)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteNonQueryAsync();
    }

    protected static void AssertReconciled((List<Account> Accounts, List<Transaction> Entries) state)
    {
        foreach (var account in state.Accounts)
        {
            var entries = state.Entries.Where(t => t.AccountId == account.Id).ToList();
            Assert.Single(entries.Where(t => t.Type == TransactionType.OpeningBalance));
            Assert.Equal(account.Balance, entries.Sum(t => t.Type switch
            {
                TransactionType.Deposit or TransactionType.TransferIn or TransactionType.OpeningBalance => t.Amount,
                TransactionType.Withdrawal or TransactionType.TransferOut => -t.Amount,
                _ => throw new InvalidOperationException("Unknown transaction type")
            }));
        }
        foreach (var group in state.Entries.Where(t => t.Type is TransactionType.TransferIn or TransactionType.TransferOut).GroupBy(t => t.ReferenceId))
        {
            Assert.NotNull(group.Key);
            Assert.NotEqual(Guid.Empty, group.Key.Value);
            Assert.Equal(2, group.Count());
            var debit = Assert.Single(group.Where(t => t.Type == TransactionType.TransferOut));
            var credit = Assert.Single(group.Where(t => t.Type == TransactionType.TransferIn));
            Assert.NotEqual(debit.AccountId, credit.AccountId);
            Assert.Equal(debit.Amount, credit.Amount);
        }
    }
}
