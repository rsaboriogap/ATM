using System;
using System.Collections.Generic;
using System.Text;

using ATM.Domain.Enums;
using ATM.Domain.Rules;

namespace ATM.Domain.Entities;

public sealed class Transaction
{
    public Guid Id { get; private set; }

    public Guid AccountId { get; private set; }

    // Position of this entry in the account's ledger: 1 for OpeningBalance, then previous + 1.
    // This is the ledger's order; never rely on insertion order or SQLite rowid.
    public long Sequence { get; private set; }

    public TransactionType Type { get; private set; }

    public decimal Amount { get; private set; }

    public decimal BalanceAfter { get; private set; }

    public Guid? ReferenceId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    // Required for EF Core materialization.
    private Transaction()
    {
    }

    public const long OpeningSequence = 1;

    public static Transaction Create(
        Guid accountId,
        long sequence,
        TransactionType type,
        decimal amount,
        decimal balanceAfter,
        Guid? referenceId = null)
    {
        if (accountId == Guid.Empty)
        {
            throw new ArgumentException(
                "Account ID is required.",
                nameof(accountId));
        }

        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type));
        }

        var isOpening = type == TransactionType.OpeningBalance;

        if (isOpening ? sequence != OpeningSequence : sequence <= OpeningSequence)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sequence),
                "OpeningBalance must be sequence 1; every later entry must follow it.");
        }

        MoneyRules.ValidateAmount(amount);
        MoneyRules.ValidateBalance(balanceAfter);

        if (isOpening && amount != balanceAfter)
        {
            throw new ArgumentException(
                "Opening balance amount must match BalanceAfter.", nameof(balanceAfter));
        }

        var isTransfer =
            type is TransactionType.TransferIn
                 or TransactionType.TransferOut;

        if (isTransfer &&
            (!referenceId.HasValue ||
             referenceId.Value == Guid.Empty))
        {
            throw new ArgumentException(
                "Transfers require a valid reference ID.",
                nameof(referenceId));
        }

        if (!isTransfer && referenceId.HasValue)
        {
            throw new ArgumentException(
                "Reference ID is only supported for transfers.",
                nameof(referenceId));
        }

        return new Transaction
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            Sequence = sequence,
            Type = type,
            Amount = amount,
            BalanceAfter = balanceAfter,
            ReferenceId = referenceId,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }
}