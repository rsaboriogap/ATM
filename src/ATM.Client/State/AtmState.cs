using ATM.Client.DTOs;
using ATM.Client.Exceptions;
using ATM.Client.Interfaces;
using ATM.Client.Models;
using Microsoft.Extensions.Logging;

namespace ATM.Client.State;

public sealed class AtmState
{
    private readonly IAtmApiClient _apiClient;
    private readonly ILogger<AtmState> _logger;

    private readonly SemaphoreSlim _operationGate = new(1, 1);

    // A write and the idempotency key it was sent with. Kept while its
    // outcome is uncertain so it can be retried with the same key.
    private sealed record PendingOperation(
        Guid IdempotencyKey,
        Func<Guid, CancellationToken, Task> Send);

    private PendingOperation? _pendingOperation;

    public IReadOnlyList<AccountDto> Accounts
    {
        get;
        private set;
    } = Array.Empty<AccountDto>();

    public IReadOnlyList<TransactionDto> Transactions
    {
        get;
        private set;
    } = Array.Empty<TransactionDto>();

    public Guid? SelectedAccountId { get; private set; }

    public bool IsBusy { get; private set; }

    public bool IsInitialized { get; private set; }

    public bool RequiresReconciliation { get; private set; }

    public bool CanRetryPendingOperation =>
        RequiresReconciliation && _pendingOperation is not null;

    public ClientError? Error { get; private set; }

    public event Action? OnChange;

    public decimal TotalBalance =>
        Accounts.Sum(account => account.Balance);

    public AccountDto? SelectedAccount =>
        Accounts.FirstOrDefault(
            account => account.Id == SelectedAccountId);

    public AtmState(
        IAtmApiClient apiClient,
        ILogger<AtmState> logger)
    {
        _apiClient = apiClient;
        _logger = logger;
    }

    public Task<bool> InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(
            async ct =>
            {
                if (IsInitialized)
                {
                    return;
                }

                await LoadSnapshotAsync(ct);

                IsInitialized = true;
            },
            cancellationToken);
    }

    public Task<bool> RefreshAsync(
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(
            async ct =>
            {
                await LoadSnapshotAsync(ct);

                IsInitialized = true;

                // The user has reviewed the persisted state; the uncertain
                // operation is no longer offered for retry.
                RequiresReconciliation = false;
                _pendingOperation = null;
            },
            cancellationToken);
    }

    public Task<bool> SelectAccountAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(
            async ct =>
            {
                var history = await _apiClient
                    .GetTransactionHistoryAsync(accountId, ct);

                SelectedAccountId = accountId;
                Transactions = history.ToArray();
            },
            cancellationToken);
    }

    // Each submission gets a new idempotency key.
    public Task<bool> DepositAsync(
        Guid accountId,
        decimal amount,
        CancellationToken cancellationToken = default)
    {
        return ExecuteMutationAsync(
            new PendingOperation(
                Guid.NewGuid(),
                async (key, ct) =>
                {
                    var result = await _apiClient.DepositAsync(
                        accountId,
                        new DepositRequest(amount),
                        key,
                        ct);

                    ApplyOperationResult(result);
                }),
            cancellationToken);
    }

    public Task<bool> WithdrawAsync(
        Guid accountId,
        decimal amount,
        CancellationToken cancellationToken = default)
    {
        return ExecuteMutationAsync(
            new PendingOperation(
                Guid.NewGuid(),
                async (key, ct) =>
                {
                    var result = await _apiClient.WithdrawAsync(
                        accountId,
                        new WithdrawalRequest(amount),
                        key,
                        ct);

                    ApplyOperationResult(result);
                }),
            cancellationToken);
    }

    public Task<bool> TransferAsync(
        Guid sourceAccountId,
        Guid destinationAccountId,
        decimal amount,
        CancellationToken cancellationToken = default)
    {
        return ExecuteMutationAsync(
            new PendingOperation(
                Guid.NewGuid(),
                async (key, ct) =>
                {
                    var result = await _apiClient.TransferAsync(
                        new TransferRequest(
                            sourceAccountId,
                            destinationAccountId,
                            amount),
                        key,
                        ct);

                    UpsertAccount(result.SourceAccount);
                    UpsertAccount(result.DestinationAccount);

                    AddTransaction(result.OutgoingTransaction);
                    AddTransaction(result.IncomingTransaction);
                }),
            cancellationToken);
    }

    /// <summary>
    /// Resends the operation whose outcome is uncertain, with the same
    /// idempotency key. The server either applies it once or returns the
    /// original result, so this can't post twice.
    /// </summary>
    public Task<bool> RetryPendingOperationAsync(
        CancellationToken cancellationToken = default)
    {
        var pending = _pendingOperation;

        return pending is null
            ? Task.FromResult(false)
            : ExecuteMutationAsync(
                pending,
                cancellationToken,
                isRetry: true);
    }

    public void ClearError()
    {
        Error = null;

        // An uncertain operation still requires reconciliation.
        NotifyStateChanged();
    }

    private async Task LoadSnapshotAsync(
        CancellationToken cancellationToken)
    {
        var accounts = await _apiClient.GetAccountsAsync(
            cancellationToken);

        var selectedAccountId =
            accounts.Any(a => a.Id == SelectedAccountId)
                ? SelectedAccountId
                : accounts.FirstOrDefault()?.Id;

        IReadOnlyList<TransactionDto> history =
            Array.Empty<TransactionDto>();

        if (selectedAccountId.HasValue)
        {
            history = await _apiClient
                .GetTransactionHistoryAsync(
                    selectedAccountId.Value,
                    cancellationToken);
        }

        // Apply the new snapshot only after all requests succeed.
        Accounts = accounts.ToArray();
        SelectedAccountId = selectedAccountId;
        Transactions = history.ToArray();
    }

    private void ApplyOperationResult(
        OperationResultDto result)
    {
        UpsertAccount(result.Account);
        AddTransaction(result.Transaction);
    }

    private void UpsertAccount(AccountDto updatedAccount)
    {
        Accounts = Accounts
            .Where(a => a.Id != updatedAccount.Id)
            .Append(updatedAccount)
            .OrderBy(a => a.Name)
            .ToArray();
    }

    private void AddTransaction(TransactionDto transaction)
    {
        if (transaction.AccountId != SelectedAccountId)
        {
            return;
        }

        Transactions = Transactions
            .Where(t => t.Id != transaction.Id)
            .Append(transaction)
            .OrderByDescending(t => t.CreatedAt)
            .ThenBy(t => t.Id)
            .ToArray();
    }

    private Task<bool> ExecuteMutationAsync(
        PendingOperation operation,
        CancellationToken cancellationToken,
        bool isRetry = false)
    {
        return ExecuteAsync(
            async ct =>
            {
                _pendingOperation = operation;

                await operation.Send(operation.IdempotencyKey, ct);

                // The server confirmed the outcome.
                _pendingOperation = null;
                RequiresReconciliation = false;
            },
            cancellationToken,
            isMutation: true,
            isRetry: isRetry);
    }

    private async Task<bool> ExecuteAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken,
        bool isMutation = false,
        bool isRetry = false)
    {
        if (!await _operationGate.WaitAsync(
            0,
            cancellationToken))
        {
            return false;
        }

        try
        {
            // While an outcome is uncertain, only its retry may be sent.
            if (isMutation && RequiresReconciliation && !isRetry)
            {
                Error = new ClientError(
                    "reconciliation_required",
                    "Refresh balances and review transaction " +
                    "history before submitting another operation.",
                    null,
                    true,
                    null);

                return false;
            }

            IsBusy = true;
            Error = null;

            NotifyStateChanged();

            await operation(cancellationToken);

            return true;
        }
        catch (ApiException exception)
        {
            Error = ClientError.FromException(exception);

            if (exception.IsMutationOutcomeUnknown)
            {
                RequiresReconciliation = true;
            }
            else if (isMutation)
            {
                // A definite rejection: the server did not apply the
                // operation (failures are never stored against the key).
                _pendingOperation = null;
                RequiresReconciliation = false;
            }

            return false;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Unexpected ATM client state error.");

            if (isMutation)
            {
                RequiresReconciliation = true;
            }

            Error = ClientError.Unexpected() with
            {
                RequiresReconciliation =
                    RequiresReconciliation
            };

            return false;
        }
        finally
        {
            IsBusy = false;

            _operationGate.Release();

            NotifyStateChanged();
        }
    }

    private void NotifyStateChanged()
    {
        var subscribers = OnChange;

        if (subscribers is null)
        {
            return;
        }

        foreach (var subscriber in subscribers.GetInvocationList())
        {
            try
            {
                ((Action)subscriber).Invoke();
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "An ATM state subscriber failed.");
            }
        }
    }
}