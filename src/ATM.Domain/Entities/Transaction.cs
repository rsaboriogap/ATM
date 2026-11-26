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

    public TransactionType Type { get; private set; }

    public decimal Amount { get; private set; }

    public decimal BalanceAfter { get; private set; }

    public Guid? ReferenceId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    // Required for EF Core materialization.
    private Transaction()
    {
    }

    public static Transaction Create(
        Guid accountId,
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

        MoneyRules.ValidateAmount(amount);
        MoneyRules.ValidateBalance(balanceAfter);

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
            Type = type,
            Amount = amount,
            BalanceAfter = balanceAfter,
            ReferenceId = referenceId,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }
}