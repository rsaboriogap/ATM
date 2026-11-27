using ATM.Application.DTOs;
using ATM.Application.Interfaces.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ATM.Infrastructure.Persistence;

public sealed class LedgerReconciliationService(AtmDbContext context)
    : ILedgerReconciliationService
{
    public async Task<IReadOnlyList<LedgerDiscrepancyDto>> GetDiscrepanciesAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await context.Database.SqlQueryRaw<ReconciliationRow>("""
            SELECT a.Id AS AccountId, a.Name AS AccountName,
                   a.Balance AS StoredBalance,
                   COALESCE(SUM(CASE WHEN t.Type IN (1,3,5) THEN t.Amount
                                     WHEN t.Type IN (2,4) THEN -t.Amount ELSE 0 END),0) AS LedgerBalance
            FROM Accounts a LEFT JOIN Transactions t ON t.AccountId = a.Id
            GROUP BY a.Id, a.Name, a.Balance
            HAVING StoredBalance <> LedgerBalance
            """).ToListAsync(cancellationToken);
        return rows.Select(row => new LedgerDiscrepancyDto(row.AccountId, row.AccountName,
            row.StoredBalance / 100m, row.LedgerBalance / 100m)).ToList();
    }

    public async Task<IReadOnlyList<string>> GetIntegrityIssuesAsync(
        CancellationToken cancellationToken = default)
    {
        // Ledger order is the explicit Sequence column, never rowid (which VACUUM or a
        // table rebuild may renumber).
        return await context.Database.SqlQueryRaw<string>("""
            WITH Ordered AS (
                SELECT t.Id, t.Type, t.Amount, t.BalanceAfter, t.Sequence,
                       LAG(t.BalanceAfter) OVER w AS PrevBalance,
                       ROW_NUMBER() OVER w AS Position
                FROM Transactions t
                WINDOW w AS (PARTITION BY t.AccountId ORDER BY t.Sequence)
            )
            SELECT 'Missing or duplicate opening balance: ' || a.Id AS Value
            FROM Accounts a LEFT JOIN Transactions t ON t.AccountId = a.Id AND t.Type = 5
            GROUP BY a.Id HAVING COUNT(t.Id) <> 1
            UNION ALL
            SELECT 'Invalid transfer reference: ' || COALESCE(ReferenceId, 'NULL') AS Value
            FROM Transactions WHERE Type IN (3,4) GROUP BY ReferenceId
            HAVING ReferenceId IS NULL OR lower(ReferenceId) = '00000000-0000-0000-0000-000000000000'
                OR COUNT(*) <> 2 OR COUNT(DISTINCT AccountId) <> 2
                OR COUNT(DISTINCT Type) <> 2 OR MIN(Amount) <> MAX(Amount)
            UNION ALL
            SELECT 'Ledger sequence gap at transaction ' || o.Id AS Value
            FROM Ordered o
            WHERE o.Sequence <> o.Position
            UNION ALL
            SELECT 'Broken running balance at transaction ' || o.Id AS Value
            FROM Ordered o
            WHERE (o.Position = 1 AND o.Type <> 5)
               OR (o.Position > 1 AND o.Type = 5)
               OR (o.Position > 1 AND o.BalanceAfter <>
                     o.PrevBalance + CASE WHEN o.Type IN (1,3) THEN o.Amount ELSE -o.Amount END)
            UNION ALL
            SELECT 'Stored balance differs from last ledger entry: ' || a.Id AS Value
            FROM Accounts a
            LEFT JOIN Transactions last
                   ON last.AccountId = a.Id
                  AND last.Sequence = (SELECT MAX(t.Sequence) FROM Transactions t WHERE t.AccountId = a.Id)
            WHERE a.Balance IS NOT last.BalanceAfter
               OR a.LedgerSequence IS NOT last.Sequence
            """).ToListAsync(cancellationToken);
    }

    public sealed class ReconciliationRow
    {
        public Guid AccountId { get; set; }
        public string AccountName { get; set; } = "";
        public long StoredBalance { get; set; }
        public long LedgerBalance { get; set; }
    }
}
