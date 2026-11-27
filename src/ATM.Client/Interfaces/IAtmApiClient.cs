using ATM.Client.DTOs;

namespace ATM.Client.Interfaces;

public interface IAtmApiClient
{
    Task<IReadOnlyList<AccountDto>> GetAccountsAsync(
        CancellationToken cancellationToken = default);

    Task<AccountDto> GetAccountAsync(
        Guid accountId,
        CancellationToken cancellationToken = default);

    // Writes send an Idempotency-Key header. Reuse the same key when retrying
    // the same operation; the server then applies it at most once.

    Task<OperationResultDto> DepositAsync(
        Guid accountId,
        DepositRequest request,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<OperationResultDto> WithdrawAsync(
        Guid accountId,
        WithdrawalRequest request,
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