using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ATM.Infrastructure.Logging;

internal sealed class DatabaseLogger : ILogger
{
    private const string OriginalFormatKey = "{OriginalFormat}";

    private readonly string _category;

    private readonly DatabaseLogQueue _queue;

    public DatabaseLogger(
        string category,
        DatabaseLogQueue queue)
    {
        _category = category;
        _queue = queue;
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
    {
        return null;
    }

    // Level filtering is applied by the logging framework using
    // the "Logging:Database:LogLevel" configuration section.
    public bool IsEnabled(LogLevel logLevel)
    {
        return logLevel != LogLevel.None;
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        string? template = null;
        Dictionary<string, string?>? properties = null;

        if (state is IReadOnlyList<KeyValuePair<string, object?>> pairs)
        {
            foreach (var pair in pairs)
            {
                if (pair.Key == OriginalFormatKey)
                {
                    template = pair.Value?.ToString();
                    continue;
                }

                properties ??= new Dictionary<string, string?>();
                properties[pair.Key] = pair.Value?.ToString();
            }
        }

        _queue.TryEnqueue(new LogEntry
        {
            TimestampUtc = DateTime.UtcNow,
            Level = logLevel.ToString(),
            Category = _category,
            EventId = eventId.Id,
            Message = formatter(state, exception),
            MessageTemplate = template,
            Properties = properties is null
                ? null
                : JsonSerializer.Serialize(properties),
            Exception = exception?.ToString(),
            TraceId = Activity.Current?.Id
        });
    }
}
