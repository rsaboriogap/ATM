using ATM.Client.Components.Forms;
using ATM.Client.DTOs;
using Bunit;
using Xunit;

namespace ATM.Client.Tests.Unit.Components;

public sealed class FinancialFormTests : BunitContext
{
    private readonly AccountDto _checking = new(
        Guid.NewGuid(),
        "Checking",
        1000m,
        "USD",
        DateTimeOffset.UtcNow);

    private readonly AccountDto _savings = new(
        Guid.NewGuid(),
        "Savings",
        1000m,
        "USD",
        DateTimeOffset.UtcNow);

    private AccountDto[] Accounts =>
        new[] { _checking, _savings };

    [Fact]
    public void Deposit_ValidAmount_InvokesCallback()
    {
        (Guid AccountId, decimal Amount)? submitted = null;

        var component = Render<DepositForm>(parameters =>
            parameters
                .Add(p => p.Accounts, Accounts)
                .Add(p => p.DefaultAccountId, _checking.Id)
                .Add(
                    p => p.OnSubmit,
                    (ValueTuple<Guid, decimal> request) =>
                    {
                        submitted = request;
                    }));

        component.Find("#operation-amount").Change("150");

        component.Find("form").Submit();

        Assert.NotNull(submitted);
        Assert.Equal(_checking.Id, submitted.Value.AccountId);
        Assert.Equal(150m, submitted.Value.Amount);
    }

    [Fact]
    public void Deposit_MissingAmount_ShowsValidation()
    {
        var component = Render<DepositForm>(parameters =>
            parameters
                .Add(p => p.Accounts, Accounts)
                .Add(p => p.DefaultAccountId, _checking.Id));

        component.Find("form").Submit();

        Assert.Contains(
            "Enter an amount.",
            component.Markup);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-10")]
    [InlineData("10.123")]
    [InlineData("1000001")]
    public void Deposit_InvalidAmount_DoesNotSubmit(
        string amount)
    {
        var submitted = false;

        var component = Render<DepositForm>(parameters =>
            parameters
                .Add(p => p.Accounts, Accounts)
                .Add(p => p.DefaultAccountId, _checking.Id)
                .Add(
                    p => p.OnSubmit,
                    (ValueTuple<Guid, decimal> request) =>
                    {
                        submitted = true;
                    }));

        component.Find("#operation-amount").Change(amount);

        component.Find("form").Submit();

        Assert.False(submitted);

        Assert.NotEmpty(
            component.FindAll(".validation-message"));
    }

    [Fact]
    public void Withdrawal_InsufficientFunds_IsRejected()
    {
        var submitted = false;

        var component = Render<WithdrawalForm>(parameters =>
            parameters
                .Add(p => p.Accounts, Accounts)
                .Add(p => p.DefaultAccountId, _checking.Id)
                .Add(
                    p => p.OnSubmit,
                    (ValueTuple<Guid, decimal> request) =>
                    {
                        submitted = true;
                    }));

        component.Find("#operation-amount").Change("1500");

        component.Find("form").Submit();

        Assert.False(submitted);

        Assert.Contains(
            "The account has insufficient funds.",
            component.Markup);
    }

    [Fact]
    public void Withdrawal_ExactBalance_IsAccepted()
    {
        decimal? submittedAmount = null;

        var component = Render<WithdrawalForm>(parameters =>
            parameters
                .Add(p => p.Accounts, Accounts)
                .Add(p => p.DefaultAccountId, _checking.Id)
                .Add(
                    p => p.OnSubmit,
                    (ValueTuple<Guid, decimal> request) =>
                    {
                        submittedAmount = request.Item2;
                    }));

        component.Find("#operation-amount").Change("1000");

        component.Find("form").Submit();

        Assert.Equal(1000m, submittedAmount);
    }

    [Fact]
    public void Transfer_ValidRequest_InvokesCallback()
    {
        TransferRequest? submitted = null;

        var component = Render<TransferForm>(parameters =>
            parameters
                .Add(p => p.Accounts, Accounts)
                .Add(p => p.DefaultAccountId, _checking.Id)
                .Add(
                    p => p.OnSubmit,
                    (TransferRequest request) =>
                    {
                        submitted = request;
                    }));

        component.Find("#transfer-amount").Change("300");

        component.Find("form").Submit();

        Assert.NotNull(submitted);
        Assert.Equal(_checking.Id, submitted.SourceAccountId);
        Assert.Equal(_savings.Id, submitted.DestinationAccountId);
        Assert.Equal(300m, submitted.Amount);
    }

    [Fact]
    public void Transfer_InsufficientFunds_IsRejected()
    {
        var component = Render<TransferForm>(parameters =>
            parameters
                .Add(p => p.Accounts, Accounts)
                .Add(p => p.DefaultAccountId, _checking.Id));

        component.Find("#transfer-amount").Change("1500");

        component.Find("form").Submit();

        Assert.Contains(
            "The account has insufficient funds.",
            component.Markup);
    }

    [Fact]
    public void Transfer_ExcludesSourceFromDestination()
    {
        var component = Render<TransferForm>(parameters =>
            parameters
                .Add(p => p.Accounts, Accounts)
                .Add(p => p.DefaultAccountId, _checking.Id));

        var options = component.FindAll(
            "#transfer-destination option");

        Assert.DoesNotContain(
            options,
            option => option.GetAttribute("value")
                == _checking.Id.ToString());
    }

    [Fact]
    public void DisabledForm_DisablesFinancialControls()
    {
        var component = Render<DepositForm>(parameters =>
            parameters
                .Add(p => p.Accounts, Accounts)
                .Add(p => p.Disabled, true));

        Assert.True(
            component.Find("fieldset")
                .HasAttribute("disabled"));

        Assert.True(
            component.Find("button[type='submit']")
                .HasAttribute("disabled"));
    }
}