using ATM.Application.Interfaces.Persistence;
using ATM.Infrastructure.Persistence;
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

        services.AddDbContext<AtmDbContext>(options =>
        {
            options.UseSqlite(connectionString);
        });

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