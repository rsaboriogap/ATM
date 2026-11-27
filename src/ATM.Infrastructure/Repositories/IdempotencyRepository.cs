using ATM.Application.Idempotency;
using ATM.Application.Interfaces.Persistence;
using ATM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ATM.Infrastructure.Repositories;

public sealed class IdempotencyRepository : IIdempotencyRepository
{
    private readonly AtmDbContext _context;

    public IdempotencyRepository(AtmDbContext context)
    {
        _context = context;
    }

    public Task<IdempotencyRecord?> FindForUpdateAsync(
        Guid key,
        CancellationToken cancellationToken = default)
    {
        if (_context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "A write transaction is required.");
        }

        return _context.IdempotencyRecords
            .AsNoTracking()
            .SingleOrDefaultAsync(
                record => record.Key == key,
                cancellationToken);
    }

    public void Add(IdempotencyRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        _context.IdempotencyRecords.Add(record);
    }
}
