using ATM.Application.DTOs;

namespace ATM.Application.Interfaces;

public interface IAtmService
{
    Task<IReadOnlyList<AccountDto>> GetAccountsAsync(
        CancellationToken cancellationToken = default);

    Task<AccountDto> GetAccountAsync(
        Guid accountId,
        CancellationToken cancellationToken = default);

    Task<OperationResultDto> DepositAsync(
        Guid accountId,
        decimal amount,
        CancellationToken cancellationToken = default);

    Task<OperationResultDto> WithdrawAsync(
        Guid accountId,
        decimal amount,
        CancellationToken cancellationToken = default);

    Task<TransferResultDto> TransferAsync(
        TransferRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TransactionDto>>
        GetTransactionHistoryAsync(
            Guid accountId,
            CancellationToken cancellationToken = default);
}