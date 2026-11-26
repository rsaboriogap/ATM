using ATM.Client.DTOs;

namespace ATM.Client.Interfaces;

public interface IAtmApiClient
{
    Task<IReadOnlyList<AccountDto>> GetAccountsAsync(
        CancellationToken cancellationToken = default);

    Task<AccountDto> GetAccountAsync(
        Guid accountId,
        CancellationToken cancellationToken = default);

    Task<OperationResultDto> DepositAsync(
        Guid accountId,
        DepositRequest request,
        CancellationToken cancellationToken = default);

    Task<OperationResultDto> WithdrawAsync(
        Guid accountId,
        WithdrawalRequest request,
        CancellationToken cancellationToken = default);

    Task<TransferResultDto> TransferAsync(
        TransferRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TransactionDto>>
        GetTransactionHistoryAsync(
            Guid accountId,
            CancellationToken cancellationToken = default);
}