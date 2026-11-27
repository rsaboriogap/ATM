using ATM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;


namespace ATM.Tests.Integration.Persistence;

internal sealed class SqliteTestDatabase : IAsyncDisposable
{
    private readonly string _databasePath =
        Path.Combine(
            Path.GetTempPath(),
            $"atm-tests-{Guid.NewGuid():N}.db");

    public AtmDbContext CreateContext()
    {
        var options =
            new DbContextOptionsBuilder<AtmDbContext>()
                .UseSqlite(
                    $"Data Source={_databasePath};" +
                    "Foreign Keys=True;" +
                    "Pooling=False;" +
                    "Default Timeout=5")
                .Options;

        return new AtmDbContext(options);
    }

    public async Task InitializeAsync()
    {
        await using var context = CreateContext();

        var reconciliationService = new LedgerReconciliationService(context);

        var initializer = new DatabaseInitializer(
            context,
            reconciliationService);

        await initializer.InitializeAsync();
    }

    public ValueTask DisposeAsync()
    {
        foreach (var suffix in new[]
        {
            "", "-wal", "-shm", "-journal"
        })
        {
            File.Delete(_databasePath + suffix);
        }

        return ValueTask.CompletedTask;
    }
}