namespace ATM.Client.DTOs;

public sealed record TransactionDto(
    Guid Id,
    Guid AccountId,
    string Type,
    decimal Amount,
    decimal BalanceAfter,
    Guid? ReferenceId,
    DateTimeOffset CreatedAt);