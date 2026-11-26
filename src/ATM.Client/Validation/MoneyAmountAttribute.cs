using System.ComponentModel.DataAnnotations;

namespace ATM.Client.Validation;

[AttributeUsage(AttributeTargets.Property)]
public sealed class MoneyAmountAttribute : ValidationAttribute
{
    public const decimal MinimumAmount = 0.01m;
    public const decimal MaximumAmount = 1_000_000m;

    public override bool IsValid(object? value)
    {
        if (value is null)
        {
            // RequiredAttribute handles null values.
            return true;
        }

        if (value is not decimal amount)
        {
            return false;
        }

        return amount >= MinimumAmount
            && amount <= MaximumAmount
            && decimal.Round(amount, 2) == amount;
    }

    public override string FormatErrorMessage(string name)
    {
        return $"{name} must be between $0.01 and " +
               "$1,000,000.00 with no fractional cents.";
    }
}