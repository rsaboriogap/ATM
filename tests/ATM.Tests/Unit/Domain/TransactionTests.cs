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
            2,
            TransactionType.Deposit,
            150m,
            1150m);

        Assert.NotEqual(Guid.Empty, transaction.Id);
        Assert.Equal(accountId, transaction.AccountId);
        Assert.Equal(2, transaction.Sequence);
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
                2,
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
            2,
            TransactionType.TransferOut,
            150m,
            850m,
            referenceId);

        var incoming = Transaction.Create(
            Guid.NewGuid(),
            2,
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
                2,
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
                2,
                TransactionType.Deposit,
                150m,
                1150m));
    }

    [Theory]
    [InlineData(TransactionType.OpeningBalance, 0)]
    [InlineData(TransactionType.OpeningBalance, 2)]
    [InlineData(TransactionType.Deposit, 1)]
    [InlineData(TransactionType.Deposit, 0)]
    [InlineData(TransactionType.Withdrawal, -1)]
    public void Create_InvalidSequence_ThrowsException(TransactionType type, long sequence)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Transaction.Create(
                Guid.NewGuid(),
                sequence,
                type,
                150m,
                150m));
    }
}
