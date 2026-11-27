using ATM.Application.Idempotency;

namespace ATM.Application.Interfaces.Persistence;

public interface IIdempotencyRepository
{
    /// <summary>
    /// Looks up a key inside the current write transaction. Refuses to run
    /// outside one, so the lookup and the insert can't race.
    /// </summary>
    Task<IdempotencyRecord?> FindForUpdateAsync(
        Guid key,
        CancellationToken cancellationToken = default);

    void Add(IdempotencyRecord record);
}
