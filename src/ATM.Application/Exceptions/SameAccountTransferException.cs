namespace ATM.Application.Exceptions;

public sealed class SameAccountTransferException
    : AtmApplicationException
{
    public SameAccountTransferException()
        : base(
            "SAME_ACCOUNT_TRANSFER",
            "Source and destination accounts must be different.")
    {
    }
}