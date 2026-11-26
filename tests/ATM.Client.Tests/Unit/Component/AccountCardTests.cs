using ATM.Client.Components.Accounts;
using ATM.Client.DTOs;
using Bunit;
using Xunit;

namespace ATM.Client.Tests.Unit.Components;

public sealed class AccountCardTests : BunitContext
{
    private readonly AccountDto _account = new(
        Guid.NewGuid(),
        "Checking",
        1000m,
        "USD",
        DateTimeOffset.UtcNow);

    [Fact]
    public void AccountCard_DisplaysNameAndBalance()
    {
        var component = Render<AccountCard>(parameters =>
            parameters.Add(p => p.Account, _account));

        Assert.Contains("Checking", component.Markup);
        Assert.Contains("$1,000.00", component.Markup);
    }

    [Fact]
    public void SelectedAccount_DisplaysSelectedState()
    {
        var component = Render<AccountCard>(parameters =>
            parameters
                .Add(p => p.Account, _account)
                .Add(p => p.IsSelected, true));

        Assert.Equal(
            "true",
            component.Find("button")
                .GetAttribute("aria-pressed"));
    }

    [Fact]
    public void AccountClick_InvokesCallback()
    {
        Guid? selectedAccount = null;

        var component = Render<AccountCard>(parameters =>
            parameters
                .Add(p => p.Account, _account)
                .Add(
                    p => p.OnSelected,
                    (Guid id) => selectedAccount = id));

        component.Find("button").Click();

        Assert.Equal(_account.Id, selectedAccount);
    }

    [Fact]
    public void DisabledAccount_DisablesButton()
    {
        var component = Render<AccountCard>(parameters =>
            parameters
                .Add(p => p.Account, _account)
                .Add(p => p.Disabled, true));

        Assert.True(
            component.Find("button")
                .HasAttribute("disabled"));
    }
}