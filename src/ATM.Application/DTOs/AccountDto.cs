namespace ATM.Application.DTOs;

public sealed record AccountDto(
    Guid Id,
    string Name,
    decimal Balance,
    string Currency,
    DateTimeOffset CreatedAt);