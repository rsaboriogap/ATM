namespace ATM.Application.DTOs;

public sealed record LedgerDiscrepancyDto(Guid AccountId, string AccountName,
    decimal StoredBalance, decimal LedgerBalance)
{
    public decimal Difference => StoredBalance - LedgerBalance;
}
