using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATM.Infrastructure.Logging;

/// <summary>
/// Logger provider that persists log entries to the Logs table.
/// Configure its levels under "Logging:Database:LogLevel".
/// </summary>
[ProviderAlias("Database")]
public sealed class DatabaseLoggerProvider : ILoggerProvider
{
    // EF Core logs every SQL command, including the INSERTs that
    // write the logs, and this namespace logs write failures.
    // Sending either back into the table would loop forever.
    private static readonly string[] ExcludedCategoryPrefixes =
    [
        "Microsoft.EntityFrameworkCore",
        typeof(DatabaseLoggerProvider).Namespace!
    ];

    private readonly DatabaseLogQueue _queue;

    public DatabaseLoggerProvider(DatabaseLogQueue queue)
    {
        _queue = queue;
    }

    public ILogger CreateLogger(string categoryName)
    {
        foreach (var prefix in ExcludedCategoryPrefixes)
        {
            if (categoryName.StartsWith(
                prefix, StringComparison.Ordinal))
            {
                return NullLogger.Instance;
            }
        }

        return new DatabaseLogger(categoryName, _queue);
    }

    public void Dispose()
    {
    }
}
