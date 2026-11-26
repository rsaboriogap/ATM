namespace ATM.Application.DTOs;

public sealed record OperationResultDto(
    AccountDto Account,
    TransactionDto Transaction);