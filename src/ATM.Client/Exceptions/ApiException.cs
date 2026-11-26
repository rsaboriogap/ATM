using System.Net;
using ATM.Client.Models;

namespace ATM.Client.Exceptions;

public sealed class ApiException : Exception
{
    public HttpStatusCode? StatusCode { get; }

    public string Code { get; }

    public string? TraceId { get; }

    public ApiProblemDetails? ProblemDetails { get; }

    public bool IsMutationOutcomeUnknown { get; }

    public ApiException(
        string message,
        string code,
        HttpStatusCode? statusCode = null,
        ApiProblemDetails? problemDetails = null,
        Exception? innerException = null,
        bool isMutationOutcomeUnknown = false)
        : base(message, innerException)
    {
        Code = code;
        StatusCode = statusCode;
        ProblemDetails = problemDetails;
        TraceId = problemDetails?.TraceId;
        IsMutationOutcomeUnknown = isMutationOutcomeUnknown;
    }
}