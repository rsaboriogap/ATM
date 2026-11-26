using System.Threading.Channels;

namespace ATM.Infrastructure.Logging;

/// <summary>
/// In-memory buffer between loggers (producers) and the
/// <see cref="DatabaseLogWriter"/> (single consumer).
/// Logging never blocks a request: when the buffer is full the
/// oldest entries are dropped.
/// </summary>
public sealed class DatabaseLogQueue
{
    private const int Capacity = 10_000;

    private readonly Channel<LogEntry> _channel =
        Channel.CreateBounded<LogEntry>(
            new BoundedChannelOptions(Capacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false
            });

    public ChannelReader<LogEntry> Reader => _channel.Reader;

    public bool TryEnqueue(LogEntry entry)
    {
        return _channel.Writer.TryWrite(entry);
    }
}
