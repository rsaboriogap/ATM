namespace ATM.Api.ErrorHandling;

public static class ErrorCodes
{
    public const string InvalidAmount =
        "invalid_amount";

    public const string SameAccountTransfer =
        "same_account_transfer";

    public const string ValidationError =
        "validation_error";

    public const string InvalidRequest =
        "invalid_request";

    public const string AccountNotFound =
        "account_not_found";

    public const string NotFound =
        "not_found";

    public const string InsufficientFunds =
        "insufficient_funds";

    public const string BalanceLimitExceeded =
        "balance_limit_exceeded";

    public const string UnsupportedMediaType =
        "unsupported_media_type";

    public const string IdempotencyKeyRequired =
        "idempotency_key_required";

    public const string IdempotencyKeyConflict =
        "idempotency_key_conflict";

    public const string DatabaseBusy =
        "database_busy";

    public const string InternalServerError =
        "internal_server_error";
}