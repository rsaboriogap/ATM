using System.Text.Json;
using ATM.Application.DTOs;
using ATM.Application.Exceptions;
using ATM.Application.Idempotency;
using ATM.Application.Interfaces;
using ATM.Application.Interfaces.Persistence;
using ATM.Application.Mappings;
using ATM.Domain.Entities;
using ATM.Domain.Enums;
using ATM.Domain.Rules;

namespace ATM.Application.Services;

public sealed class AtmService : IAtmService
{
    private static readonly JsonSerializerOptions IdempotencyJsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly IAccountRepository _accountRepository;
    private readonly ITransactionRepository _transactionRepository;
    private readonly IIdempotencyRepository _idempotencyRepository;
    private readonly IUnitOfWork _unitOfWork;

    public AtmService(
        IAccountRepository accountRepository,
        ITransactionRepository transactionRepository,
        IIdempotencyRepository idempotencyRepository,
        IUnitOfWork unitOfWork)
    {
        _accountRepository = accountRepository;
        _transactionRepository = transactionRepository;
        _idempotencyRepository = idempotencyRepository;
        _unitOfWork = unitOfWork;
    }

    // Queries

    public async Task<IReadOnlyList<AccountDto>> GetAccountsAsync(
        CancellationToken cancellationToken = default)
    {
        var accounts = await _accountRepository
            .GetAllAsync(cancellationToken);

        return accounts
            .Select(account => account.ToDto())
            .ToList();
    }

    public async Task<AccountDto> GetAccountAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        var account = await GetRequiredAccountAsync(
            accountId,
            cancellationToken);

        return account.ToDto();
    }

    public async Task<IReadOnlyList<TransactionDto>>
        GetTransactionHistoryAsync(
            Guid accountId,
            CancellationToken cancellationToken = default)
    {
        await GetRequiredAccountAsync(
            accountId,
            cancellationToken);

        var transactions = await _transactionRepository
            .GetByAccountIdAsync(
                accountId,
                cancellationToken);

        return transactions
            .Select(transaction => transaction.ToDto())
            .ToList();
    }

    // Commands

    public Task<OperationResultDto> DepositAsync(
        Guid accountId,
        decimal amount,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default) =>
        ExecuteAccountOperationAsync(
            accountId,
            amount,
            TransactionType.Deposit,
            idempotencyKey,
            cancellationToken);

    public Task<OperationResultDto> WithdrawAsync(
        Guid accountId,
        decimal amount,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default) =>
        ExecuteAccountOperationAsync(
            accountId,
            amount,
            TransactionType.Withdrawal,
            idempotencyKey,
            cancellationToken);

    public Task<TransferResultDto> TransferAsync(
        TransferRequest request,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        MoneyRules.ValidateAmount(request.Amount);

        if (request.SourceAccountId ==
            request.DestinationAccountId)
        {
            throw new SameAccountTransferException();
        }

        var fingerprint =
            $"Transfer|{request.SourceAccountId:N}|" +
            $"{request.DestinationAccountId:N}|{ToCents(request.Amount)}";

        return ExecuteIdempotentAsync(
            idempotencyKey,
            fingerprint,
            async ct =>
            {
                var source = await GetRequiredAccountForUpdateAsync(
                    request.SourceAccountId, ct);

                var destination =
                    await GetRequiredAccountForUpdateAsync(
                        request.DestinationAccountId, ct);

                var entries = Account.Transfer(
                    source,
                    destination,
                    request.Amount);

                _transactionRepository.Add(entries.Outgoing);
                _transactionRepository.Add(entries.Incoming);

                return new TransferResultDto(
                    entries.ReferenceId,
                    source.ToDto(),
                    destination.ToDto(),
                    entries.Outgoing.ToDto(),
                    entries.Incoming.ToDto());
            },
            cancellationToken);
    }

    // Private helpers

    // Runs the operation at most once per key. The key lookup, the ledger
    // entries and the idempotency record share one write transaction, so they
    // commit or roll back together. BEGIN IMMEDIATE serializes writers, so two
    // concurrent requests with the same key can't both post.
    private Task<T> ExecuteIdempotentAsync<T>(
        Guid idempotencyKey,
        string fingerprint,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        if (idempotencyKey == Guid.Empty)
        {
            throw new IdempotencyKeyRequiredException();
        }

        return _unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                var existing = await _idempotencyRepository
                    .FindForUpdateAsync(idempotencyKey, ct);

                if (existing is not null)
                {
                    if (existing.Fingerprint != fingerprint)
                    {
                        throw new IdempotencyKeyConflictException(
                            idempotencyKey);
                    }

                    // Replay: return the original result, write nothing.
                    return JsonSerializer.Deserialize<T>(
                        existing.ResponseJson,
                        IdempotencyJsonOptions)
                        ?? throw new InvalidOperationException(
                            "Stored idempotent response is empty.");
                }

                var result = await operation(ct);

                _idempotencyRepository.Add(new IdempotencyRecord(
                    idempotencyKey,
                    fingerprint,
                    JsonSerializer.Serialize(
                        result,
                        IdempotencyJsonOptions)));

                return result;
            },
            cancellationToken);
    }

    // Amounts are validated to 2 decimals; cents make 100m and 100.00m equal.
    private static long ToCents(decimal amount) =>
        decimal.ToInt64(amount * 100m);

    private Task<OperationResultDto> ExecuteAccountOperationAsync(
        Guid accountId,
        decimal amount,
        TransactionType transactionType,
        Guid idempotencyKey,
        CancellationToken cancellationToken)
    {
        MoneyRules.ValidateAmount(amount);

        var fingerprint =
            $"{transactionType}|{accountId:N}|{ToCents(amount)}";

        return ExecuteIdempotentAsync(
            idempotencyKey,
            fingerprint,
            async ct =>
            {
                var account =
                    await GetRequiredAccountForUpdateAsync(
                        accountId, ct);

                var transaction = transactionType switch
                {
                    TransactionType.Deposit => account.Deposit(amount),
                    TransactionType.Withdrawal => account.Withdraw(amount),
                    _ => throw new InvalidOperationException(
                        "Unsupported transaction type.")
                };

                _transactionRepository.Add(transaction);

                return new OperationResultDto(
                    account.ToDto(),
                    transaction.ToDto());
            },
            cancellationToken);
    }

    private async Task<Account> GetRequiredAccountAsync(
        Guid accountId,
        CancellationToken cancellationToken)
    {
        return await _accountRepository.GetByIdAsync(
            accountId,
            cancellationToken)
            ?? throw new AccountNotFoundException(accountId);
    }

    private async Task<Account> GetRequiredAccountForUpdateAsync(
        Guid accountId,
        CancellationToken cancellationToken)
    {
        return await _accountRepository.GetForUpdateAsync(
            accountId,
            cancellationToken)
            ?? throw new AccountNotFoundException(accountId);
    }
}