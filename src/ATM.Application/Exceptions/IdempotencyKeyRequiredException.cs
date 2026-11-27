namespace ATM.Application.Exceptions;

public sealed class IdempotencyKeyRequiredException
    : AtmApplicationException
{
    public IdempotencyKeyRequiredException()
        : base(
            "IDEMPOTENCY_KEY_REQUIRED",
            "An Idempotency-Key header with a non-empty GUID is required.")
    {
    }
}
