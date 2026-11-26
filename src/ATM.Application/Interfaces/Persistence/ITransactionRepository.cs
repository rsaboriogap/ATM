using ATM.Domain.Entities;

namespace ATM.Application.Interfaces.Persistence;

public interface ITransactionRepository
{
    Task<IReadOnlyList<Transaction>> GetByAccountIdAsync(
        Guid accountId,
        CancellationToken cancellationToken = default);

    void Add(Transaction transaction);
}