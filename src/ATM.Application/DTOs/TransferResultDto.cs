namespace ATM.Application.DTOs;

public sealed record TransferResultDto(
    Guid ReferenceId,
    AccountDto SourceAccount,
    AccountDto DestinationAccount,
    TransactionDto OutgoingTransaction,
    TransactionDto IncomingTransaction);