using ATM.Client.Components.Shared;
using ATM.Client.Models;
using Bunit;
using Xunit;

namespace ATM.Client.Tests.Unit.Components;

public sealed class AlertTests : BunitContext
{
    [Fact]
    public void ErrorAlert_DisplaysMessageAndTraceId()
    {
        var error = new ClientError(
            "insufficient_funds",
            "The account has insufficient funds.",
            "test-trace-id",
            false,
            null);

        var component = Render<ErrorAlert>(parameters =>
            parameters.Add(p => p.Error, error));

        Assert.Contains(error.Message, component.Markup);
        Assert.Contains("test-trace-id", component.Markup);
    }

    [Fact]
    public void ErrorAlert_Dismiss_InvokesCallback()
    {
        var dismissed = false;

        var error = new ClientError(
            "network_error",
            "Connection failed.",
            null,
            false,
            null);

        var component = Render<ErrorAlert>(parameters =>
            parameters
                .Add(p => p.Error, error)
                .Add(p => p.OnDismiss, () =>
                {
                    dismissed = true;
                }));

        component.Find("button").Click();

        Assert.True(dismissed);
    }

    [Fact]
    public void SuccessAlert_DisplaysConfirmation()
    {
        var component = Render<SuccessAlert>(parameters =>
            parameters.Add(
                p => p.Message,
                "Deposit completed successfully."));

        Assert.Contains(
            "Deposit completed successfully.",
            component.Markup);
    }

    [Fact]
    public void LoadingIndicator_DisplaysProcessingState()
    {
        var component = Render<LoadingIndicator>(
            parameters => parameters
                .Add(p => p.Visible, true));

        Assert.Contains(
            "Processing your request...",
            component.Markup);

        Assert.Single(
            component.FindAll(".spinner-border"));
    }

    [Fact]
    public void ReconciliationAlert_InvokesRefresh()
    {
        var refreshed = false;

        var component = Render<ReconciliationAlert>(
            parameters => parameters
                .Add(p => p.Visible, true)
                .Add(p => p.OnRefresh, () =>
                {
                    refreshed = true;
                }));

        component.Find("button").Click();

        Assert.True(refreshed);

        Assert.Contains(
            "Transaction status uncertain",
            component.Markup);
    }
}