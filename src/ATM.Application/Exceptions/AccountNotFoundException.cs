namespace ATM.Application.Exceptions;

public sealed class AccountNotFoundException
    : AtmApplicationException
{
    public Guid AccountId { get; }

    public AccountNotFoundException(Guid accountId)
        : base(
            "ACCOUNT_NOT_FOUND",
            $"Account '{accountId}' was not found.")
    {
        AccountId = accountId;
    }
}