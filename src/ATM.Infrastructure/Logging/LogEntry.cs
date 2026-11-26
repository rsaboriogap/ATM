namespace ATM.Infrastructure.Logging;

/// <summary>
/// A single application log record persisted to the Logs table.
/// </summary>
public sealed class LogEntry
{
    public long Id { get; set; }

    public DateTime TimestampUtc { get; set; }

    public string Level { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public int EventId { get; set; }

    /// <summary>The fully rendered message.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>The raw template, e.g. "Path: {Path}".</summary>
    public string? MessageTemplate { get; set; }

    /// <summary>Structured placeholder values as a JSON object.</summary>
    public string? Properties { get; set; }

    public string? Exception { get; set; }

    public string? TraceId { get; set; }
}
