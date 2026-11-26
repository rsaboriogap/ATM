using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace ATM.Api.ErrorHandling;

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

        var traceId = Activity.Current?.Id
            ?? httpContext.TraceIdentifier;

        LogException(
            httpContext,
            exception,
            mapping,
            traceId);

        var problemDetails = new ProblemDetails
        {
            Type = "about:blank",
            Title = mapping.Title,
            Status = mapping.StatusCode,
            Detail = mapping.Detail,
            Instance = httpContext.Request.Path.Value
        };

        problemDetails.Extensions["code"] = mapping.Code;
        problemDetails.Extensions["traceId"] = traceId;

        httpContext.Response.StatusCode = mapping.StatusCode;

        var written = await _problemDetailsService.TryWriteAsync(
            new ProblemDetailsContext
            {
                HttpContext = httpContext,
                ProblemDetails = problemDetails,
                Exception = exception
            });

        if (!written)
        {
            await httpContext.Response.WriteAsJsonAsync(
                problemDetails,
                options: null,
                contentType: "application/problem+json",
                cancellationToken: cancellationToken);
        }

        return true;
    }

    private void LogException(
        HttpContext httpContext,
        Exception exception,
        ExceptionMapping mapping,
        string traceId)
    {
        if (mapping.StatusCode ==
            StatusCodes.Status500InternalServerError)
        {
            _logger.LogError(
                exception,
                "Unexpected ATM error. " +
                "Method: {Method}, Path: {Path}, " +
                "Code: {ErrorCode}, Status: {StatusCode}, " +
                "TraceId: {TraceId}",
                httpContext.Request.Method,
                httpContext.Request.Path,
                mapping.Code,
                mapping.StatusCode,
                traceId);

            return;
        }

        if (mapping.StatusCode ==
            StatusCodes.Status503ServiceUnavailable)
        {
            _logger.LogWarning(
                exception,
                "ATM database temporarily unavailable. " +
                "Method: {Method}, Path: {Path}, " +
                "Code: {ErrorCode}, TraceId: {TraceId}",
                httpContext.Request.Method,
                httpContext.Request.Path,
                mapping.Code,
                traceId);

            return;
        }

        _logger.LogInformation(
            "ATM request rejected. " +
            "Method: {Method}, Path: {Path}, " +
            "Code: {ErrorCode}, Status: {StatusCode}, " +
            "TraceId: {TraceId}",
            httpContext.Request.Method,
            httpContext.Request.Path,
            mapping.Code,
            mapping.StatusCode,
            traceId);
    }
}