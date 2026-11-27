namespace ATM.Application.Idempotency;

/// <summary>
/// Remembers the outcome of a write request so a retry with the same
/// Idempotency-Key returns the original result instead of posting again.
/// Stored in the same transaction as the ledger entries it describes.
/// </summary>
public sealed class IdempotencyRecord
{
    // EF Core
    private IdempotencyRecord()
    {
    }

    public IdempotencyRecord(
        Guid key,
        string fingerprint,
        string responseJson)
    {
        if (key == Guid.Empty)
        {
            throw new ArgumentException(
                "The idempotency key cannot be empty.",
                nameof(key));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(responseJson);

        Key = key;
        Fingerprint = fingerprint;
        ResponseJson = responseJson;
        CreatedAt = DateTime.UtcNow;
    }

    public Guid Key { get; private set; }

    /// <summary>
    /// The operation and its parameters, e.g. "Withdrawal|{accountId}|10000".
    /// A retry must match it exactly.
    /// </summary>
    public string Fingerprint { get; private set; } = string.Empty;

    public string ResponseJson { get; private set; } = string.Empty;

    public DateTime CreatedAt { get; private set; }
}
