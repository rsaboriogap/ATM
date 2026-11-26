using ATM.Domain.Entities;

namespace ATM.Application.Interfaces.Persistence;

public interface IAccountRepository
{
    Task<Account?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Account>> GetAllAsync(
        CancellationToken cancellationToken = default);

    Task<Account?> GetForUpdateAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}