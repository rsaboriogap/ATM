using ATM.Application.DTOs;
using ATM.Application.Exceptions;
using ATM.Application.Interfaces;
using ATM.Application.Interfaces.Persistence;
using ATM.Application.Mappings;
using ATM.Domain.Entities;
using ATM.Domain.Enums;
using ATM.Domain.Rules;

namespace ATM.Application.Services;

public sealed class AtmService : IAtmService
{
    private readonly IAccountRepository _accountRepository;
    private readonly ITransactionRepository _transactionRepository;
    private readonly IUnitOfWork _unitOfWork;

    public AtmService(
        IAccountRepository accountRepository,
        ITransactionRepository transactionRepository,
        IUnitOfWork unitOfWork)
    {
        _accountRepository = accountRepository;
        _transactionRepository = transactionRepository;
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
        CancellationToken cancellationToken = default) =>
        ExecuteAccountOperationAsync(
            accountId, amount, TransactionType.Deposit, cancellationToken);

    public Task<OperationResultDto> WithdrawAsync(
        Guid accountId,
        decimal amount,
        CancellationToken cancellationToken = default) =>
        ExecuteAccountOperationAsync(
            accountId, amount, TransactionType.Withdrawal, cancellationToken);

    public Task<TransferResultDto> TransferAsync(
        TransferRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        MoneyRules.ValidateAmount(request.Amount);

        if (request.SourceAccountId ==
            request.DestinationAccountId)
        {
            throw new SameAccountTransferException();
        }

        return _unitOfWork.ExecuteInTransactionAsync(
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

    private Task<OperationResultDto> ExecuteAccountOperationAsync(
        Guid accountId,
        decimal amount,
        TransactionType transactionType,
        CancellationToken cancellationToken)
    {
        MoneyRules.ValidateAmount(amount);

        return _unitOfWork.ExecuteInTransactionAsync(
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