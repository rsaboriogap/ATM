using ATM.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ATM.Infrastructure.Logging;

/// <summary>
/// Background service that drains <see cref="DatabaseLogQueue"/>
/// and inserts the entries into the Logs table in batches.
/// </summary>
internal sealed class DatabaseLogWriter : BackgroundService
{
    private const int MaxBatchSize = 200;

    private readonly DatabaseLogQueue _queue;

    private readonly IServiceScopeFactory _scopeFactory;

    // This category is excluded from the database provider,
    // so failures here only reach console/debug output.
    private readonly ILogger<DatabaseLogWriter> _logger;

    public DatabaseLogWriter(
        DatabaseLogQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<DatabaseLogWriter> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        var batch = new List<LogEntry>(MaxBatchSize);

        try
        {
            while (await _queue.Reader.WaitToReadAsync(stoppingToken))
            {
                await FlushAsync(batch);
            }
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }

        // Persist whatever was logged before shutdown.
        await FlushAsync(batch);
    }

    private async Task FlushAsync(List<LogEntry> batch)
    {
        while (true)
        {
            while (batch.Count < MaxBatchSize &&
                _queue.Reader.TryRead(out var entry))
            {
                batch.Add(entry);
            }

            if (batch.Count == 0)
            {
                return;
            }

            await SaveBatchAsync(batch);
        }
    }

    private async Task SaveBatchAsync(List<LogEntry> batch)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();

            var context = scope.ServiceProvider
                .GetRequiredService<AtmDbContext>();

            context.Logs.AddRange(batch);

            // Not cancelled on shutdown: the batch is small and
            // we want it persisted.
            await context.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            // Never let a logging failure (e.g. a locked SQLite
            // database) crash the app; the batch is dropped.
            _logger.LogWarning(
                exception,
                "Failed to write {Count} log entries to the database.",
                batch.Count);
        }
        finally
        {
            batch.Clear();
        }
    }
}
