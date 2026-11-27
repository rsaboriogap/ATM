using ATM.Api.ErrorHandling;
using ATM.Application.Exceptions;
using ATM.Domain.Exceptions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ATM.Tests.Unit.Api;

public sealed class ExceptionMapperTests
{
    [Fact]
    public void Map_InvalidAmount_Returns400()
    {
        var exception =
            new InvalidAmountException("Invalid amount.");

        var result = ExceptionMapper.Map(exception);

        Assert.Equal(400, result.StatusCode);
        Assert.Equal(ErrorCodes.InvalidAmount, result.Code);
    }

    [Fact]
    public void Map_AccountNotFound_Returns404()
    {
        var exception =
            new AccountNotFoundException(Guid.NewGuid());

        var result = ExceptionMapper.Map(exception);

        Assert.Equal(404, result.StatusCode);
        Assert.Equal(ErrorCodes.AccountNotFound, result.Code);
    }

    [Fact]
    public void Map_SameAccountTransfer_Returns400()
    {
        var result = ExceptionMapper.Map(
            new SameAccountTransferException());

        Assert.Equal(400, result.StatusCode);
        Assert.Equal(
            ErrorCodes.SameAccountTransfer,
            result.Code);
    }

    [Fact]
    public void Map_IdempotencyKeyRequired_Returns400()
    {
        var result = ExceptionMapper.Map(
            new IdempotencyKeyRequiredException());

        Assert.Equal(400, result.StatusCode);
        Assert.Equal(
            ErrorCodes.IdempotencyKeyRequired,
            result.Code);
    }

    [Fact]
    public void Map_IdempotencyKeyConflict_Returns422()
    {
        var result = ExceptionMapper.Map(
            new IdempotencyKeyConflictException(Guid.NewGuid()));

        Assert.Equal(422, result.StatusCode);
        Assert.Equal(
            ErrorCodes.IdempotencyKeyConflict,
            result.Code);
    }

    [Fact]
    public void Map_InsufficientFunds_Returns409()
    {
        var result = ExceptionMapper.Map(
            new InsufficientFundsException());

        Assert.Equal(409, result.StatusCode);
        Assert.Equal(
            ErrorCodes.InsufficientFunds,
            result.Code);
    }

    [Fact]
    public void Map_BalanceLimitExceeded_Returns409()
    {
        var result = ExceptionMapper.Map(
            new BalanceLimitExceededException());

        Assert.Equal(409, result.StatusCode);
        Assert.Equal(
            ErrorCodes.BalanceLimitExceeded,
            result.Code);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    public void Map_SqliteBusyOrLocked_Returns503(
        int errorCode)
    {
        var exception = new SqliteException(
            "Database temporarily unavailable.",
            errorCode);

        var result = ExceptionMapper.Map(exception);

        Assert.Equal(503, result.StatusCode);
        Assert.Equal(ErrorCodes.DatabaseBusy, result.Code);
    }

    [Fact]
    public void Map_WrappedSqliteException_Returns503()
    {
        var exception = new DbUpdateException(
            "Persistence error.",
            new SqliteException("Database busy.", 5));

        var result = ExceptionMapper.Map(exception);

        Assert.Equal(503, result.StatusCode);
        Assert.Equal(ErrorCodes.DatabaseBusy, result.Code);
    }

    [Fact]
    public void Map_SqliteConstraint_Returns500()
    {
        var exception = new SqliteException(
            "Constraint violation.",
            19);

        var result = ExceptionMapper.Map(exception);

        Assert.Equal(500, result.StatusCode);
        Assert.Equal(
            ErrorCodes.InternalServerError,
            result.Code);
    }

    [Fact]
    public void Map_UnexpectedException_HidesInternalDetails()
    {
        var exception = new InvalidOperationException(
            "Sensitive internal database information.");

        var result = ExceptionMapper.Map(exception);

        Assert.Equal(500, result.StatusCode);

        Assert.Equal(
            ErrorCodes.InternalServerError,
            result.Code);

        Assert.DoesNotContain(
            "Sensitive",
            result.Detail);

        Assert.Equal(
            "An unexpected error occurred.",
            result.Detail);
    }
}