namespace ATM.Application.Exceptions;

public sealed class IdempotencyKeyConflictException
    : AtmApplicationException
{
    public IdempotencyKeyConflictException(Guid key)
        : base(
            "IDEMPOTENCY_KEY_CONFLICT",
            $"Idempotency key '{key}' was already used for a different request.")
    {
    }
}
