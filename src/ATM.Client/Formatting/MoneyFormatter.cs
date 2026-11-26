using System.Globalization;

namespace ATM.Client.Formatting;

public static class MoneyFormatter
{
    private static readonly CultureInfo UsCulture =
        CultureInfo.GetCultureInfo("en-US");

    public static string Format(decimal amount)
    {
        return amount.ToString("C2", UsCulture);
    }

    public static string FormatSigned(
        decimal amount,
        bool isCredit)
    {
        var sign = isCredit ? "+" : "-";

        return $"{sign}{Format(Math.Abs(amount))}";
    }
}