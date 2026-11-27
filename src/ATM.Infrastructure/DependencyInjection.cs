using ATM.Application.Interfaces.Persistence;
using ATM.Infrastructure.Persistence;
using ATM.Infrastructure.Persistence.Interceptors;
using ATM.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ATM.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString =
            configuration.GetConnectionString("AtmDatabase")
            ?? throw new InvalidOperationException(
                "AtmDatabase connection string is missing.");

        services.AddSingleton<SqliteConnectionInterceptor>();
        services.AddDbContext<AtmDbContext>((provider, options) =>
        {
            options.UseSqlite(connectionString);
            options.AddInterceptors(provider.GetRequiredService<SqliteConnectionInterceptor>());
        });

        services.AddScoped<ILedgerReconciliationService, LedgerReconciliationService>();

        services.AddScoped<DatabaseInitializer>();

        services.AddScoped<
            IAccountRepository,
            AccountRepository>();

        services.AddScoped<
            ITransactionRepository,
            TransactionRepository>();

        services.AddScoped<
            IUnitOfWork,
            EfUnitOfWork>();

        return services;
    }
}