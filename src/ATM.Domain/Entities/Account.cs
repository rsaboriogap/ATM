using System;
using System.Collections.Generic;
using System.Text;

using ATM.Domain.Exceptions;
using ATM.Domain.Rules;

namespace ATM.Domain.Entities;

public sealed class Account
{
    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public decimal Balance { get; private set; }

    public string Currency { get; private set; } = MoneyRules.Currency;

    public DateTimeOffset CreatedAt { get; private set; }

    // Required for EF Core materialization.
    private Account()
    {
    }

    public static Account Create(
        string name,
        decimal initialBalance = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        MoneyRules.ValidateBalance(initialBalance);

        return new Account
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Balance = initialBalance,
            Currency = MoneyRules.Currency,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public void Deposit(decimal amount)
    {
        MoneyRules.ValidateAmount(amount);

        var newBalance = Balance + amount;

        MoneyRules.ValidateBalance(newBalance);

        Balance = newBalance;
    }

    public void Withdraw(decimal amount)
    {
        MoneyRules.ValidateAmount(amount);

        if (amount > Balance)
        {
            throw new InsufficientFundsException();
        }

        Balance -= amount;
    }
}