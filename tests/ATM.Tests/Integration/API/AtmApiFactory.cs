using ATM.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ATM.Tests.Integration.Api;

internal sealed class AtmApiFactory
    : WebApplicationFactory<Program>
{
    private readonly bool _ownsDatabase;
    private readonly int _defaultTimeout;

    public string DatabasePath { get; }

    public AtmApiFactory(
        string? databasePath = null,
        int defaultTimeout = 5)
    {
        _ownsDatabase = databasePath is null;
        _defaultTimeout = defaultTimeout;

        DatabasePath = databasePath ?? Path.Combine(
            Path.GetTempPath(),
            $"atm-api-tests-{Guid.NewGuid():N}.db");
    }

    protected override void ConfigureWebHost(
        IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<AtmDbContext>();

            services.RemoveAll<
                DbContextOptions<AtmDbContext>>();

            services.RemoveAll<
                IDbContextOptionsConfiguration<AtmDbContext>>();

            var connectionString =
                new SqliteConnectionStringBuilder
                {
                    DataSource = DatabasePath,
                    ForeignKeys = true,
                    Pooling = false,
                    DefaultTimeout = _defaultTimeout
                }.ToString();

            services.AddDbContext<AtmDbContext>(options =>
            {
                options.UseSqlite(connectionString);
            });
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();

        if (_ownsDatabase)
        {
            DeleteDatabaseFiles(DatabasePath);
        }
    }

    public static void DeleteDatabaseFiles(string databasePath)
    {
        foreach (var suffix in new[]
        {
            "", "-wal", "-shm", "-journal"
        })
        {
            File.Delete(databasePath + suffix);
        }
    }
}