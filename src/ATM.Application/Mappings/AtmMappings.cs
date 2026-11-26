using ATM.Application.DTOs;
using ATM.Domain.Entities;

namespace ATM.Application.Mappings;

internal static class AtmMappings
{
    public static AccountDto ToDto(this Account account)
    {
        return new AccountDto(
            account.Id,
            account.Name,
            account.Balance,
            account.Currency,
            account.CreatedAt);
    }

    public static TransactionDto ToDto(
        this Transaction transaction)
    {
        return new TransactionDto(
            transaction.Id,
            transaction.AccountId,
            transaction.Type.ToString(),
            transaction.Amount,
            transaction.BalanceAfter,
            transaction.ReferenceId,
            transaction.CreatedAt);
    }
}