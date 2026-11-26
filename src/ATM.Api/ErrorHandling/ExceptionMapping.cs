namespace ATM.Api.ErrorHandling;

public sealed record ExceptionMapping(
    int StatusCode,
    string Code,
    string Title,
    string Detail);