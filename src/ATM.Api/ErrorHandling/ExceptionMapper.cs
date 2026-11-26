using ATM.Application.Exceptions;
using ATM.Domain.Exceptions;
using Microsoft.Data.Sqlite;

namespace ATM.Api.ErrorHandling;

public static class ExceptionMapper
{
    public static ExceptionMapping Map(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            InvalidAmountException => new ExceptionMapping(
                StatusCodes.Status400BadRequest,
                ErrorCodes.InvalidAmount,
                "Invalid amount",
                exception.Message),

            SameAccountTransferException => new ExceptionMapping(
                StatusCodes.Status400BadRequest,
                ErrorCodes.SameAccountTransfer,
                "Invalid transfer",
                exception.Message),

            AccountNotFoundException => new ExceptionMapping(
                StatusCodes.Status404NotFound,
                ErrorCodes.AccountNotFound,
                "Account not found",
                exception.Message),

            InsufficientFundsException => new ExceptionMapping(
                StatusCodes.Status409Conflict,
                ErrorCodes.InsufficientFunds,
                "Insufficient funds",
                exception.Message),

            BalanceLimitExceededException => new ExceptionMapping(
                StatusCodes.Status409Conflict,
                ErrorCodes.BalanceLimitExceeded,
                "Balance limit exceeded",
                exception.Message),

            BadHttpRequestException
            {
                StatusCode: StatusCodes.Status415UnsupportedMediaType
            } => new ExceptionMapping(
                StatusCodes.Status415UnsupportedMediaType,
                ErrorCodes.UnsupportedMediaType,
                "Unsupported media type",
                "The request must use a supported content type."),

            BadHttpRequestException
            {
                StatusCode: StatusCodes.Status400BadRequest
            } => new ExceptionMapping(
                StatusCodes.Status400BadRequest,
                ErrorCodes.InvalidRequest,
                "Invalid request",
                "The request could not be processed."),

            _ when IsDatabaseBusy(exception) => new ExceptionMapping(
                StatusCodes.Status503ServiceUnavailable,
                ErrorCodes.DatabaseBusy,
                "Database temporarily unavailable",
                "The database is temporarily busy. " +
                "Verify the operation status before trying again."),

            _ => new ExceptionMapping(
                StatusCodes.Status500InternalServerError,
                ErrorCodes.InternalServerError,
                "Internal server error",
                "An unexpected error occurred.")
        };
    }

    private static bool IsDatabaseBusy(Exception exception)
    {
        const int SqliteBusy = 5;
        const int SqliteLocked = 6;

        Exception? current = exception;

        while (current is not null)
        {
            if (current is SqliteException sqliteException &&
                sqliteException.SqliteErrorCode
                    is SqliteBusy or SqliteLocked)
            {
                return true;
            }

            current = current.InnerException;
        }

        return false;
    }
}