using System;
using System.Collections.Generic;
using System.Text;
using ATM.Domain.Exceptions;

namespace ATM.Domain.Rules;

public static class MoneyRules
{
    public const decimal MinimumAmount = 0.01m;

    public const decimal MaximumAmount = 1_000_000m;

    public const decimal MaximumBalance = 1_000_000_000m;

    public const int DecimalPlaces = 2;

    public const string Currency = "USD";

    public static void ValidateAmount(decimal amount)
    {
        if (amount < MinimumAmount)
        {
            throw new InvalidAmountException(
                "Amount must be greater than zero.");
        }

        if (amount > MaximumAmount)
        {
            throw new InvalidAmountException(
                $"Amount cannot exceed {MaximumAmount}.");
        }

        if (decimal.Round(amount, DecimalPlaces) != amount)
        {
            throw new InvalidAmountException(
                "Amount cannot have more than two decimal places.");
        }
    }

    public static void ValidateBalance(decimal balance)
    {
        if (balance < 0)
        {
            throw new InvalidAmountException(
                "Balance cannot be negative.");
        }

        if (balance > MaximumBalance)
        {
            throw new BalanceLimitExceededException();
        }

        if (decimal.Round(balance, DecimalPlaces) != balance)
        {
            throw new InvalidAmountException(
                "Balance cannot have more than two decimal places.");
        }
    }
}