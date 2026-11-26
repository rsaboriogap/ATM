using System.ComponentModel.DataAnnotations;
using System.Globalization;
using ATM.Client.Models;
using ATM.Client.Validation;
using Xunit;

namespace ATM.Client.Tests.Unit.Validation;

public sealed class MoneyAmountTests
{
    [Theory]
    [InlineData("0.01", true)]
    [InlineData("100", true)]
    [InlineData("1000000", true)]
    [InlineData("0", false)]
    [InlineData("-10", false)]
    [InlineData("0.001", false)]
    [InlineData("10.123", false)]
    [InlineData("1000000.01", false)]
    public void MoneyAmount_ValidatesFinancialRules(
        string value,
        bool expected)
    {
        var amount = decimal.Parse(
            value,
            CultureInfo.InvariantCulture);

        var attribute = new MoneyAmountAttribute();

        Assert.Equal(expected, attribute.IsValid(amount));
    }

    [Fact]
    public void TransferModel_RejectsSameAccount()
    {
        var accountId = Guid.NewGuid();

        var model = new TransferFormModel
        {
            SourceAccountId = accountId,
            DestinationAccountId = accountId,
            Amount = 100m
        };

        var results = new List<ValidationResult>();

        var valid = Validator.TryValidateObject(
            model,
            new ValidationContext(model),
            results,
            validateAllProperties: true);

        Assert.False(valid);

        Assert.Contains(
            results,
            result => result.MemberNames.Contains(
                nameof(model.DestinationAccountId)));
    }

    [Fact]
    public void TransferModel_AcceptsDifferentAccounts()
    {
        var model = new TransferFormModel
        {
            SourceAccountId = Guid.NewGuid(),
            DestinationAccountId = Guid.NewGuid(),
            Amount = 100m
        };

        var valid = Validator.TryValidateObject(
            model,
            new ValidationContext(model),
            new List<ValidationResult>(),
            validateAllProperties: true);

        Assert.True(valid);
    }
}