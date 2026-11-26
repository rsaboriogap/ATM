using System;
using System.Collections.Generic;
using System.Text;
using ATM.Domain.Entities;
using ATM.Domain.Enums;
using ATM.Domain.Exceptions;
using Xunit;

namespace ATM.Tests.Unit.Domain;

public sealed class TransactionTests
{
    [Fact]
    public void Create_ValidDeposit_CreatesTransaction()
    {
        var accountId = Guid.NewGuid();

        var transaction = Transaction.Create(
            accountId,
            TransactionType.Deposit,
            150m,
            1150m);

        Assert.NotEqual(Guid.Empty, transaction.Id);
        Assert.Equal(accountId, transaction.AccountId);
        Assert.Equal(TransactionType.Deposit, transaction.Type);
        Assert.Equal(150m, transaction.Amount);
        Assert.Equal(1150m, transaction.BalanceAfter);
        Assert.Null(transaction.ReferenceId);
    }

    [Fact]
    public void Create_TransferWithoutReference_ThrowsException()
    {
        Assert.Throws<ArgumentException>(() =>
            Transaction.Create(
                Guid.NewGuid(),
                TransactionType.TransferOut,
                150m,
                850m));
    }

    [Fact]
    public void Create_TransferPair_SharesReferenceId()
    {
        var referenceId = Guid.NewGuid();

        var outgoing = Transaction.Create(
            Guid.NewGuid(),
            TransactionType.TransferOut,
            150m,
            850m,
            referenceId);

        var incoming = Transaction.Create(
            Guid.NewGuid(),
            TransactionType.TransferIn,
            150m,
            1150m,
            referenceId);

        Assert.Equal(outgoing.ReferenceId, incoming.ReferenceId);
        Assert.Equal(outgoing.Amount, incoming.Amount);
        Assert.NotEqual(outgoing.Id, incoming.Id);
    }

    [Fact]
    public void Create_InvalidAmount_ThrowsException()
    {
        Assert.Throws<InvalidAmountException>(() =>
            Transaction.Create(
                Guid.NewGuid(),
                TransactionType.Deposit,
                -150m,
                850m));
    }

    [Fact]
    public void Create_EmptyAccountId_ThrowsException()
    {
        Assert.Throws<ArgumentException>(() =>
            Transaction.Create(
                Guid.Empty,
                TransactionType.Deposit,
                150m,
                1150m));
    }
}