using ATM.Client.Exceptions;

namespace ATM.Client.Models;

public sealed record ClientError(
    string Code,
    string Message,
    string? TraceId,
    bool RequiresReconciliation,
    IReadOnlyDictionary<string, string[]>? ValidationErrors)
{
    public static ClientError FromException(
        ApiException exception)
    {
        return new ClientError(
            exception.Code,
            exception.Message,
            exception.TraceId,
            exception.IsMutationOutcomeUnknown,
            exception.ProblemDetails?.Errors);
    }

    public static ClientError Unexpected()
    {
        return new ClientError(
            "unexpected_error",
            "An unexpected error occurred.",
            null,
            false,
            null);
    }
}