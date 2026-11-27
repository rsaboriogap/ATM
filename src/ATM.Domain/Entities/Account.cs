using ATM.Domain.Enums;
using ATM.Domain.Exceptions;
using ATM.Domain.Rules;

namespace ATM.Domain.Entities;

public sealed record AccountOpening(Account Account, Transaction OpeningEntry);

public sealed record TransferEntries(
    Guid ReferenceId,
    Transaction Outgoing,
    Transaction Incoming);

public sealed class Account
{
    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public decimal Balance { get; private set; }

    // Sequence of the latest ledger entry. Like Balance, it is derived from the ledger.
    public long LedgerSequence { get; private set; }

    public string Currency { get; private set; } = MoneyRules.Currency;

    public DateTimeOffset CreatedAt { get; private set; }

    // Required for EF Core materialization.
    private Account()
    {
    }

    // The only way to create an account: it always comes with its OpeningBalance ledger entry.
    public static AccountOpening Open(
        string name,
        decimal openingBalance)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        // The opening entry is a ledger entry, so it must be a valid amount (>= 0.01).
        MoneyRules.ValidateAmount(openingBalance);
        MoneyRules.ValidateBalance(openingBalance);

        var account = new Account
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Balance = openingBalance,
            LedgerSequence = Transaction.OpeningSequence,
            Currency = MoneyRules.Currency,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var openingEntry = Transaction.Create(
            account.Id,
            account.LedgerSequence,
            TransactionType.OpeningBalance,
            openingBalance,
            account.Balance);

        return new AccountOpening(account, openingEntry);
    }

    // Every balance change returns the ledger entry that records it.
    public Transaction Deposit(decimal amount)
    {
        Credit(amount);

        return AppendEntry(TransactionType.Deposit, amount);
    }

    public Transaction Withdraw(decimal amount)
    {
        Debit(amount);

        return AppendEntry(TransactionType.Withdrawal, amount);
    }

    public static TransferEntries Transfer(
        Account source,
        Account destination,
        decimal amount)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        if (source.Id == destination.Id)
        {
            throw new ArgumentException(
                "Source and destination accounts must differ.",
                nameof(destination));
        }

        MoneyRules.ValidateAmount(amount);

        // Validate both sides before modifying either account.
        MoneyRules.ValidateBalance(destination.Balance + amount);

        if (amount > source.Balance)
        {
            throw new InsufficientFundsException();
        }

        source.Debit(amount);
        destination.Credit(amount);

        var referenceId = Guid.NewGuid();

        return new TransferEntries(
            referenceId,
            source.AppendEntry(TransactionType.TransferOut, amount, referenceId),
            destination.AppendEntry(TransactionType.TransferIn, amount, referenceId));
    }

    // Call after Balance has been updated: the entry records the new balance at the next sequence.
    private Transaction AppendEntry(
        TransactionType type,
        decimal amount,
        Guid? referenceId = null)
    {
        var entry = Transaction.Create(
            Id,
            LedgerSequence + 1,
            type,
            amount,
            Balance,
            referenceId);

        LedgerSequence = entry.Sequence;

        return entry;
    }

    private void Credit(decimal amount)
    {
        MoneyRules.ValidateAmount(amount);

        var newBalance = Balance + amount;

        MoneyRules.ValidateBalance(newBalance);

        Balance = newBalance;
    }

    private void Debit(decimal amount)
    {
        MoneyRules.ValidateAmount(amount);

        if (amount > Balance)
        {
            throw new InsufficientFundsException();
        }

        Balance -= amount;
    }
}
