namespace ATM.Application.Exceptions;

public abstract class AtmApplicationException : Exception
{
    public string Code { get; }

    protected AtmApplicationException(
        string code,
        string message)
        : base(message)
    {
        Code = code;
    }
}