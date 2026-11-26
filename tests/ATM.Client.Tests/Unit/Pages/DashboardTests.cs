using ATM.Client.DTOs;
using ATM.Client.Exceptions;
using ATM.Client.Interfaces;
using ATM.Client.Pages;
using ATM.Client.State;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using Xunit.Sdk;

namespace ATM.Client.Tests.Unit.Pages;

public sealed class DashboardTests : BunitContext
{
    private readonly Mock<IAtmApiClient> _apiClient = new();

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

    private readonly AtmState _state;

    public DashboardTests()
    {
        _apiClient
            .Setup(api => api.GetAccountsAsync(
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new[] { _checking, _savings });

        _apiClient
            .Setup(api => api.GetTransactionHistoryAsync(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                Array.Empty<TransactionDto>());

        _state = new AtmState(
            _apiClient.Object,
            NullLogger<AtmState>.Instance);

        Services.AddSingleton(_state);
    }

    [Fact]
    public void Dashboard_DisplaysBothAccounts()
    {
        var component = Render<Dashboard>();

        component.WaitForAssertion(() =>
        {
            Assert.Equal(
                2,
                component.FindAll(
                    "[data-testid='account-card']").Count);

            Assert.Contains(
                "$2,000.00",
                component.Markup);
        });
    }

    [Fact]
    public void Dashboard_SelectAccount_UpdatesHistory()
    {
        var component = Render<Dashboard>();

        component.WaitForAssertion(() =>
            Assert.Equal(
                2,
                component.FindAll(
                    "[data-testid='account-card']").Count));

        component.FindAll(
            "[data-testid='account-card']")[1].Click();

        component.WaitForAssertion(() =>
        {
            Assert.Equal(
                _savings.Id,
                _state.SelectedAccountId);

            _apiClient.Verify(
                api => api.GetTransactionHistoryAsync(
                    _savings.Id,
                    It.IsAny<CancellationToken>()),
                Times.Once);
        });
    }

    [Fact]
    public void Dashboard_SwitchesToTransferForm()
    {
        var component = Render<Dashboard>();

        component.WaitForElement(
            "[data-testid='transfer-tab']").Click();

        Assert.Contains(
            "Transfer funds",
            component.Markup);

        Assert.NotNull(
            component.Find("#transfer-source"));
    }

    [Fact]
    public void Dashboard_Deposit_UpdatesBalance()
    {
        var transaction = new TransactionDto(
            Guid.NewGuid(),
            _checking.Id,
            "Deposit",
            150m,
            1150m,
            null,
            DateTimeOffset.UtcNow);

        _apiClient
            .Setup(api => api.DepositAsync(
                _checking.Id,
                new DepositRequest(150m),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new OperationResultDto(
                    _checking with { Balance = 1150m },
                    transaction));

        var component = Render<Dashboard>();

        component.WaitForElement(
            "#operation-amount").Change("150");

        component.Find("form").Submit();

        component.WaitForAssertion(() =>
        {
            Assert.Contains(
                "$1,150.00",
                component.Markup);

            Assert.Contains(
                "$2,150.00",
                component.Markup);

            Assert.Contains(
                "completed successfully",
                component.Markup);

            Assert.Equal(
                1150m,
                _state.SelectedAccount!.Balance);
        });
    }

    [Fact]
    public void Dashboard_UncertainOperation_DisablesForms()
    {
        _apiClient
            .Setup(api => api.DepositAsync(
                It.IsAny<Guid>(),
                It.IsAny<DepositRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(
                new ApiException(
                    "Connection interrupted.",
                    "network_error",
                    isMutationOutcomeUnknown: true));

        var component = Render<Dashboard>();

        component.WaitForElement(
            "#operation-amount").Change("100");

        component.Find("form").Submit();

        component.WaitForAssertion(() =>
        {
            Assert.True(_state.RequiresReconciliation);

            Assert.Contains(
                "Transaction status uncertain",
                component.Markup);

            Assert.True(
                component.Find("fieldset")
                    .HasAttribute("disabled"));

            Assert.True(
                component.Find(
                    "[data-testid='transfer-tab']")
                    .HasAttribute("disabled"));
        });
    }
}