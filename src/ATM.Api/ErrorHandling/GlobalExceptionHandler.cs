using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace ATM.Api.ErrorHandling;

/// <summary>
/// Turns exceptions into ProblemDetails responses. <see cref="ExceptionMapper"/> decides
/// status, code and message; traceId and instance are added for every problem response
/// in one place (AddProblemDetails in ApiServiceExtensions).
/// </summary>
public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    private readonly IProblemDetailsService _problemDetailsService;

    public GlobalExceptionHandler(
        ILogger<GlobalExceptionHandler> logger,
        IProblemDetailsService problemDetailsService)
    {
        _logger = logger;
        _problemDetailsService = problemDetailsService;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (httpContext.Response.HasStarted)
        {
            return false;
        }

        var mapping = ExceptionMapper.Map(exception);

        // Expected business rejections (4xx) are routine; only server-side
        // failures carry the exception.
        var level = mapping.StatusCode switch
        {
            StatusCodes.Status500InternalServerError => LogLevel.Error,
            StatusCodes.Status503ServiceUnavailable => LogLevel.Warning,
            _ => LogLevel.Information
        };

        _logger.Log(
            level,
            level >= LogLevel.Warning ? exception : null,
            "ATM request failed. Method: {Method}, Path: {Path}, " +
            "Code: {ErrorCode}, Status: {StatusCode}",
            httpContext.Request.Method,
            httpContext.Request.Path,
            mapping.Code,
            mapping.StatusCode);

        var problemDetails = new ProblemDetails
        {
            Status = mapping.StatusCode,
            Title = mapping.Title,
            Detail = mapping.Detail
        };

        problemDetails.Extensions["code"] = mapping.Code;

        httpContext.Response.StatusCode = mapping.StatusCode;

        // If the client's Accept header rules out JSON, nothing is written,
        // but the status code still reports the outcome.
        await _problemDetailsService.TryWriteAsync(
            new ProblemDetailsContext
            {
                HttpContext = httpContext,
                ProblemDetails = problemDetails,
                Exception = exception
            });

        return true;
    }
}
