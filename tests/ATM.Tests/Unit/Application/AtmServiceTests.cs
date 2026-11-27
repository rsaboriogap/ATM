using ATM.Application.DTOs;
using ATM.Application.Exceptions;
using ATM.Application.Interfaces.Persistence;
using ATM.Application.Services;
using ATM.Domain.Entities;
using ATM.Domain.Enums;
using ATM.Domain.Exceptions;
using ATM.Domain.Rules;
using Moq;
using Xunit;

namespace ATM.Tests.Unit.Application;

public sealed class AtmServiceTests
{
    private readonly Mock<IAccountRepository> _accounts = new();

    private readonly Mock<ITransactionRepository> _transactions = new();

    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    private readonly AtmService _service;

    public AtmServiceTests()
    {
        ConfigureUnitOfWork<OperationResultDto>();
        ConfigureUnitOfWork<TransferResultDto>();

        _service = new AtmService(
            _accounts.Object,
            _transactions.Object,
            _unitOfWork.Object);
    }

    private void ConfigureUnitOfWork<TResult>()
    {
        _unitOfWork
            .Setup(uow => uow.ExecuteInTransactionAsync<TResult>(
                It.IsAny<Func<CancellationToken, Task<TResult>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((
                Func<CancellationToken, Task<TResult>> operation,
                CancellationToken ct) => operation(ct));
    }

    private Account CreateAccount(string name)
    {
        var account = Account.Open(name, 1000m).Account;

        _accounts
            .Setup(repository => repository.GetByIdAsync(
                account.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        _accounts
            .Setup(repository => repository.GetForUpdateAsync(
                account.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        return account;
    }

    [Fact]
    public async Task GetAccounts_ReturnsAccountDtos()
    {
        var checking = CreateAccount("Checking");
        var savings = CreateAccount("Savings");

        _accounts
            .Setup(repository => repository.GetAllAsync(
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { checking, savings });

        var result = await _service.GetAccountsAsync();

        Assert.Equal(2, result.Count);
        Assert.All(result, a => Assert.Equal(1000m, a.Balance));
    }

    [Fact]
    public async Task GetAccount_ExistingAccount_ReturnsDto()
    {
        var account = CreateAccount("Checking");

        var result = await _service.GetAccountAsync(account.Id);

        Assert.Equal(account.Id, result.Id);
        Assert.Equal(1000m, result.Balance);
    }

    [Fact]
    public async Task GetAccount_UnknownAccount_ThrowsException()
    {
        await Assert.ThrowsAsync<AccountNotFoundException>(
            () => _service.GetAccountAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Deposit_ValidAmount_RegistersTransaction()
    {
        var account = CreateAccount("Checking");

        var result = await _service.DepositAsync(
            account.Id,
            150m);

        Assert.Equal(1150m, result.Account.Balance);
        Assert.Equal(150m, result.Transaction.Amount);
        Assert.Equal("Deposit", result.Transaction.Type);

        _transactions.Verify(repository => repository.Add(
            It.Is<Transaction>(t =>
                t.AccountId == account.Id &&
                t.Type == TransactionType.Deposit &&
                t.Amount == 150m)),
            Times.Once);
    }

    [Fact]
    public async Task Deposit_InvalidAmount_DoesNotStartTransaction()
    {
        var account = CreateAccount("Checking");

        await Assert.ThrowsAsync<InvalidAmountException>(
            () => _service.DepositAsync(
                account.Id,
                -100m));

        _unitOfWork.Verify(uow =>
            uow.ExecuteInTransactionAsync<OperationResultDto>(
                It.IsAny<Func<CancellationToken,
                    Task<OperationResultDto>>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        Assert.Equal(1000m, account.Balance);
    }

    [Fact]
    public async Task Deposit_UnknownAccount_ThrowsException()
    {
        await Assert.ThrowsAsync<AccountNotFoundException>(
            () => _service.DepositAsync(
                Guid.NewGuid(),
                100m));

        _transactions.Verify(
            repository => repository.Add(It.IsAny<Transaction>()),
            Times.Never);
    }

    [Fact]
    public async Task Withdraw_ValidAmount_DecreasesBalance()
    {
        var account = CreateAccount("Checking");

        var result = await _service.WithdrawAsync(
            account.Id,
            250m);

        Assert.Equal(750m, result.Account.Balance);
        Assert.Equal("Withdrawal", result.Transaction.Type);
    }

    [Fact]
    public async Task Withdraw_InsufficientFunds_DoesNotRegisterMovement()
    {
        var account = CreateAccount("Checking");

        await Assert.ThrowsAsync<InsufficientFundsException>(
            () => _service.WithdrawAsync(
                account.Id,
                1500m));

        Assert.Equal(1000m, account.Balance);

        _transactions.Verify(
            repository => repository.Add(It.IsAny<Transaction>()),
            Times.Never);
    }

    [Fact]
    public async Task Transfer_ValidAccounts_RegistersTwoMovements()
    {
        var source = CreateAccount("Checking");
        var destination = CreateAccount("Savings");

        var result = await _service.TransferAsync(
            new TransferRequest(
                source.Id,
                destination.Id,
                300m));

        Assert.Equal(700m, result.SourceAccount.Balance);
        Assert.Equal(1300m, result.DestinationAccount.Balance);

        Assert.Equal(
            result.ReferenceId,
            result.OutgoingTransaction.ReferenceId);

        Assert.Equal(
            result.ReferenceId,
            result.IncomingTransaction.ReferenceId);

        Assert.Equal("TransferOut",
            result.OutgoingTransaction.Type);

        Assert.Equal("TransferIn",
            result.IncomingTransaction.Type);

        _transactions.Verify(
            repository => repository.Add(It.IsAny<Transaction>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task Transfer_SameAccount_ThrowsException()
    {
        var account = CreateAccount("Checking");

        await Assert.ThrowsAsync<SameAccountTransferException>(
            () => _service.TransferAsync(
                new TransferRequest(
                    account.Id,
                    account.Id,
                    100m)));

        _unitOfWork.Verify(uow =>
            uow.ExecuteInTransactionAsync<TransferResultDto>(
                It.IsAny<Func<CancellationToken,
                    Task<TransferResultDto>>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Transfer_UnknownDestination_PreservesSource()
    {
        var source = CreateAccount("Checking");

        await Assert.ThrowsAsync<AccountNotFoundException>(
            () => _service.TransferAsync(
                new TransferRequest(
                    source.Id,
                    Guid.NewGuid(),
                    100m)));

        Assert.Equal(1000m, source.Balance);

        _transactions.Verify(
            repository => repository.Add(It.IsAny<Transaction>()),
            Times.Never);
    }

    [Fact]
    public async Task Transfer_InsufficientFunds_PreservesBothAccounts()
    {
        var source = CreateAccount("Checking");
        var destination = CreateAccount("Savings");

        await Assert.ThrowsAsync<InsufficientFundsException>(
            () => _service.TransferAsync(
                new TransferRequest(
                    source.Id,
                    destination.Id,
                    1500m)));

        Assert.Equal(1000m, source.Balance);
        Assert.Equal(1000m, destination.Balance);

        _transactions.Verify(
            repository => repository.Add(It.IsAny<Transaction>()),
            Times.Never);
    }

    [Fact]
    public async Task Transfer_DestinationLimit_PreservesBothAccounts()
    {
        var source = CreateAccount("Checking");

        // Opening balance is capped at MaximumAmount; reach the
        // maximum balance through deposits.
        var destination = Account.Open(
            "Savings",
            MoneyRules.MaximumAmount).Account;

        while (destination.Balance < MoneyRules.MaximumBalance)
        {
            destination.Deposit(MoneyRules.MaximumAmount);
        }

        _accounts.Setup(repository =>
            repository.GetForUpdateAsync(
                destination.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(destination);

        await Assert.ThrowsAsync<BalanceLimitExceededException>(
            () => _service.TransferAsync(
                new TransferRequest(
                    source.Id,
                    destination.Id,
                    100m)));

        Assert.Equal(1000m, source.Balance);
        Assert.Equal(1_000_000_000m, destination.Balance);
    }

    [Fact]
    public async Task GetHistory_ExistingAccount_ReturnsTransactions()
    {
        var account = CreateAccount("Checking");

        var transaction = Transaction.Create(
            account.Id,
            2,
            TransactionType.Deposit,
            100m,
            1100m);

        _transactions.Setup(repository =>
            repository.GetByAccountIdAsync(
                account.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { transaction });

        var result = await _service
            .GetTransactionHistoryAsync(account.Id);

        Assert.Single(result);
        Assert.Equal(transaction.Id, result[0].Id);
    }

    [Fact]
    public async Task GetHistory_UnknownAccount_ThrowsException()
    {
        await Assert.ThrowsAsync<AccountNotFoundException>(
            () => _service.GetTransactionHistoryAsync(
                Guid.NewGuid()));

        _transactions.Verify(repository =>
            repository.GetByAccountIdAsync(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }
}