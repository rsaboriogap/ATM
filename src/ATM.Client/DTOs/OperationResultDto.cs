namespace ATM.Client.DTOs;

public sealed record OperationResultDto(
    AccountDto Account,
    TransactionDto Transaction);