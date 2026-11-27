using ATM.Application.DTOs;

namespace ATM.Application.Interfaces.Persistence;

public interface ILedgerReconciliationService
{
    Task<IReadOnlyList<LedgerDiscrepancyDto>> GetDiscrepanciesAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetIntegrityIssuesAsync(
        CancellationToken cancellationToken = default);
}
