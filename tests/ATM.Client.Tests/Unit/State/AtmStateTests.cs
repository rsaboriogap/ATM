using ATM.Client.DTOs;
using ATM.Client.Exceptions;
using ATM.Client.Interfaces;
using ATM.Client.State;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Net;
using System.Timers;
using Xunit;
using Xunit.Sdk;

namespace ATM.Client.Tests.Unit.State;

public sealed class AtmStateTests
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

    public AtmStateTests()
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
    }

    [Fact]
    public async Task Initialize_LoadsAccountsAndHistory()
    {
        var state = CreateState();

        var success = await state.InitializeAsync();

        Assert.True(success);
        Assert.True(state.IsInitialized);
        Assert.False(state.IsBusy);
        Assert.Equal(2, state.Accounts.Count);
        Assert.Equal(2000m, state.TotalBalance);
        Assert.Equal(_checking.Id, state.SelectedAccountId);
        Assert.Null(state.Error);
    }

    [Fact]
    public async Task InitializeTwice_DoesNotReloadAccounts()
    {
        var state = CreateState();

        await state.InitializeAsync();
        await state.InitializeAsync();

        _apiClient.Verify(
            api => api.GetAccountsAsync(
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SelectAccount_LoadsSelectedHistory()
    {
        var state = await CreateInitializedStateAsync();

        var transaction = CreateTransaction(
            _savings.Id,
            "Deposit",
            100m,
            1100m);

        _apiClient
            .Setup(api => api.GetTransactionHistoryAsync(
                _savings.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { transaction });

        var success = await state.SelectAccountAsync(
            _savings.Id);

        Assert.True(success);
        Assert.Equal(_savings.Id, state.SelectedAccountId);

        Assert.Equal(
            transaction,
            Assert.Single(state.Transactions));
    }

    [Fact]
    public async Task Deposit_UpdatesAccountAndHistory()
    {
        var state = await CreateInitializedStateAsync();

        var transaction = CreateTransaction(
            _checking.Id,
            "Deposit",
            150m,
            1150m);

        _apiClient
            .Setup(api => api.DepositAsync(
                _checking.Id,
                new DepositRequest(150m),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new OperationResultDto(
                    _checking with { Balance = 1150m },
                    transaction));

        var success = await state.DepositAsync(
            _checking.Id,
            150m);

        Assert.True(success);
        Assert.Equal(1150m, state.SelectedAccount!.Balance);
        Assert.Equal(2150m, state.TotalBalance);

        Assert.Equal(
            transaction,
            Assert.Single(state.Transactions));

        _apiClient.Verify(
            api => api.GetAccountsAsync(
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Withdraw_UpdatesAccountAndHistory()
    {
        var state = await CreateInitializedStateAsync();

        var transaction = CreateTransaction(
            _checking.Id,
            "Withdrawal",
            250m,
            750m);

        _apiClient
            .Setup(api => api.WithdrawAsync(
                _checking.Id,
                new WithdrawalRequest(250m),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new OperationResultDto(
                    _checking with { Balance = 750m },
                    transaction));

        var success = await state.WithdrawAsync(
            _checking.Id,
            250m);

        Assert.True(success);
        Assert.Equal(750m, state.SelectedAccount!.Balance);
        Assert.Equal(1750m, state.TotalBalance);
        Assert.Single(state.Transactions);
    }

    [Fact]
    public async Task Transfer_UpdatesBothAccounts()
    {
        var state = await CreateInitializedStateAsync();

        var referenceId = Guid.NewGuid();

        var outgoing = CreateTransaction(
            _checking.Id,
            "TransferOut",
            300m,
            700m,
            referenceId);

        var incoming = CreateTransaction(
            _savings.Id,
            "TransferIn",
            300m,
            1300m,
            referenceId);

        _apiClient
            .Setup(api => api.TransferAsync(
                new TransferRequest(
                    _checking.Id,
                    _savings.Id,
                    300m),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new TransferResultDto(
                    referenceId,
                    _checking with { Balance = 700m },
                    _savings with { Balance = 1300m },
                    outgoing,
                    incoming));

        var success = await state.TransferAsync(
            _checking.Id,
            _savings.Id,
            300m);

        Assert.True(success);

        Assert.Equal(
            700m,
            state.Accounts.Single(
                a => a.Id == _checking.Id).Balance);

        Assert.Equal(
            1300m,
            state.Accounts.Single(
                a => a.Id == _savings.Id).Balance);

        Assert.Equal(2000m, state.TotalBalance);

        Assert.Equal(
            outgoing,
            Assert.Single(state.Transactions));
    }

    [Fact]
    public async Task RejectedWithdrawal_PreservesLocalBalance()
    {
        var state = await CreateInitializedStateAsync();

        _apiClient
            .Setup(api => api.WithdrawAsync(
                It.IsAny<Guid>(),
                It.IsAny<WithdrawalRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(
                new ApiException(
                    "Insufficient funds.",
                    "insufficient_funds",
                    HttpStatusCode.Conflict));

        var success = await state.WithdrawAsync(
            _checking.Id,
            1500m);

        Assert.False(success);
        Assert.Equal(1000m, state.SelectedAccount!.Balance);
        Assert.Empty(state.Transactions);
        Assert.False(state.IsBusy);
        Assert.False(state.RequiresReconciliation);

        Assert.Equal(
            "insufficient_funds",
            state.Error?.Code);
    }

    [Fact]
    public async Task UnknownMutation_BlocksAdditionalOperations()
    {
        var state = await CreateInitializedStateAsync();

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

        var firstResult = await state.DepositAsync(
            _checking.Id,
            100m);

        var secondResult = await state.DepositAsync(
            _checking.Id,
            100m);

        Assert.False(firstResult);
        Assert.False(secondResult);
        Assert.True(state.RequiresReconciliation);

        Assert.Equal(
            "reconciliation_required",
            state.Error?.Code);

        _apiClient.Verify(
            api => api.DepositAsync(
                It.IsAny<Guid>(),
                It.IsAny<DepositRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Refresh_ReconcilesPersistedBalances()
    {
        var state = await CreateInitializedStateAsync();

        _apiClient
            .Setup(api => api.DepositAsync(
                It.IsAny<Guid>(),
                It.IsAny<DepositRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(
                new ApiException(
                    "Response was lost.",
                    "network_error",
                    isMutationOutcomeUnknown: true));

        await state.DepositAsync(_checking.Id, 100m);

        Assert.True(state.RequiresReconciliation);

        var transaction = CreateTransaction(
            _checking.Id,
            "Deposit",
            100m,
            1100m);

        _apiClient
            .Setup(api => api.GetAccountsAsync(
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new[]
                {
                    _checking with { Balance = 1100m },
                    _savings
                });

        _apiClient
            .Setup(api => api.GetTransactionHistoryAsync(
                _checking.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { transaction });

        var success = await state.RefreshAsync();

        Assert.True(success);
        Assert.False(state.RequiresReconciliation);
        Assert.Equal(1100m, state.SelectedAccount!.Balance);
        Assert.Single(state.Transactions);
        Assert.Null(state.Error);
    }

    [Fact]
    public async Task FailedRefresh_DoesNotClearReconciliation()
    {
        var state = await CreateInitializedStateAsync();

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

        await state.DepositAsync(_checking.Id, 100m);

        _apiClient
            .Setup(api => api.GetAccountsAsync(
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(
                new ApiException(
                    "Server unavailable.",
                    "network_error"));

        var success = await state.RefreshAsync();

        Assert.False(success);
        Assert.True(state.RequiresReconciliation);
        Assert.Equal(1000m, state.SelectedAccount!.Balance);
    }

    [Fact]
    public async Task ConcurrentOperations_RejectDuplicateSubmission()
    {
        var state = await CreateInitializedStateAsync();

        var pending = new TaskCompletionSource<OperationResultDto>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        _apiClient
            .Setup(api => api.DepositAsync(
                It.IsAny<Guid>(),
                It.IsAny<DepositRequest>(),
                It.IsAny<CancellationToken>()))
            .Returns(pending.Task);

        var firstOperation = state.DepositAsync(
            _checking.Id,
            100m);

        Assert.True(state.IsBusy);

        try
        {
            var secondResult = await state.DepositAsync(
                _checking.Id,
                100m);

            Assert.False(secondResult);
        }
        finally
        {
            pending.TrySetResult(
                new OperationResultDto(
                    _checking with { Balance = 1100m },
                    CreateTransaction(
                        _checking.Id,
                        "Deposit",
                        100m,
                        1100m)));
        }

        Assert.True(await firstOperation);
        Assert.False(state.IsBusy);

        _apiClient.Verify(
            api => api.DepositAsync(
                It.IsAny<Guid>(),
                It.IsAny<DepositRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task StateChange_NotifiesSubscribers()
    {
        var state = CreateState();

        var busyStates = new List<bool>();

        state.OnChange += () =>
            busyStates.Add(state.IsBusy);

        await state.InitializeAsync();

        Assert.Contains(true, busyStates);
        Assert.False(busyStates.Last());
    }

    [Fact]
    public async Task ClearError_DoesNotClearReconciliation()
    {
        var state = await CreateInitializedStateAsync();

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

        await state.DepositAsync(_checking.Id, 100m);

        state.ClearError();

        Assert.Null(state.Error);
        Assert.True(state.RequiresReconciliation);
    }

    private AtmState CreateState()
    {
        return new AtmState(
            _apiClient.Object,
            NullLogger<AtmState>.Instance);
    }

    private async Task<AtmState> CreateInitializedStateAsync()
    {
        var state = CreateState();

        await state.InitializeAsync();

        return state;
    }

    private static TransactionDto CreateTransaction(
        Guid accountId,
        string type,
        decimal amount,
        decimal balanceAfter,
        Guid? referenceId = null)
    {
        return new TransactionDto(
            Guid.NewGuid(),
            accountId,
            type,
            amount,
            balanceAfter,
            referenceId,
            DateTimeOffset.UtcNow);
    }
}