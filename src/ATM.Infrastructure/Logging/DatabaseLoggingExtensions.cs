using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace ATM.Infrastructure.Logging;

public static class DatabaseLoggingExtensions
{
    /// <summary>
    /// Persists log entries to the Logs table of the ATM database.
    /// Requires <c>AddInfrastructure</c> to register AtmDbContext.
    /// </summary>
    public static ILoggingBuilder AddDatabaseLogger(
        this ILoggingBuilder builder)
    {
        builder.Services.TryAddSingleton<DatabaseLogQueue>();

        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                ILoggerProvider,
                DatabaseLoggerProvider>());

        builder.Services.AddHostedService<DatabaseLogWriter>();

        return builder;
    }
}
