using ATM.Application.DTOs;

namespace ATM.Application.Interfaces;

public interface IAtmService
{
    Task<IReadOnlyList<AccountDto>> GetAccountsAsync(
        CancellationToken cancellationToken = default);

    Task<AccountDto> GetAccountAsync(
        Guid accountId,
        CancellationToken cancellationToken = default);

    // Write operations are idempotent per key: a repeated key with the same
    // request returns the original result without posting again.

    Task<OperationResultDto> DepositAsync(
        Guid accountId,
        decimal amount,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<OperationResultDto> WithdrawAsync(
        Guid accountId,
        decimal amount,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<TransferResultDto> TransferAsync(
        TransferRequest request,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TransactionDto>>
        GetTransactionHistoryAsync(
            Guid accountId,
            CancellationToken cancellationToken = default);
}