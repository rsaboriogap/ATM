using ATM.Domain.Entities;
using ATM.Domain.Enums;
using ATM.Infrastructure.Persistence;

namespace ATM.Tests.Integration.Persistence;

internal static class LedgerTestExtensions
{
    /// <summary>
    /// Ledger rows produced by operations, excluding the OpeningBalance
    /// entries written when the initializer seeds accounts.
    /// </summary>
    public static IQueryable<Transaction> ActivityTransactions(
        this AtmDbContext context)
    {
        return context.Transactions
            .Where(t => t.Type != TransactionType.OpeningBalance);
    }
}
