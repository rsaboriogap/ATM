using System;
using System.Collections.Generic;
using System.Text;
using ATM.Domain.Exceptions;
using ATM.Domain.Rules;
using Xunit;

namespace ATM.Tests.Unit.Domain;

public sealed class MoneyRulesTests
{
    [Fact]
    public void ValidateAmount_MinimumAmount_IsAccepted()
    {
        MoneyRules.ValidateAmount(0.01m);
    }

    [Fact]
    public void ValidateAmount_MaximumAmount_IsAccepted()
    {
        MoneyRules.ValidateAmount(1_000_000m);
    }

    [Fact]
    public void ValidateAmount_ExcessPrecision_ThrowsException()
    {
        Assert.Throws<InvalidAmountException>(
            () => MoneyRules.ValidateAmount(10.123m));
    }

    [Fact]
    public void ValidateAmount_AboveMaximum_ThrowsException()
    {
        Assert.Throws<InvalidAmountException>(
            () => MoneyRules.ValidateAmount(1_000_000.01m));
    }

    [Fact]
    public void ValidateBalance_ZeroBalance_IsAccepted()
    {
        MoneyRules.ValidateBalance(0m);
    }
}