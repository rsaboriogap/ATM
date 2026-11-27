using System.Diagnostics;
using ATM.Api.ErrorHandling;
using Microsoft.AspNetCore.Mvc;

namespace ATM.Api.Configuration;

public static class ApiServiceExtensions
{
    public static IServiceCollection AddApiServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOpenApi();

        services.AddValidation();

        services.AddExceptionHandler<GlobalExceptionHandler>();

        // The single place that completes every problem response, whether it
        // comes from GlobalExceptionHandler or from the framework (validation,
        // unknown route, unsupported media type).
        services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = context =>
            {
                var problem = context.ProblemDetails;

                problem.Instance ??=
                    context.HttpContext.Request.Path.Value;

                problem.Extensions["traceId"] =
                    Activity.Current?.Id
                    ?? context.HttpContext.TraceIdentifier;

                // GlobalExceptionHandler sets a specific code; framework
                // problems get one derived from the status.
                problem.Extensions.TryAdd(
                    "code",
                    ResolveErrorCode(problem));
            };
        });

        services.AddCors(options =>
        {
            var corsOptions = configuration
                .GetSection(CorsOptions.SectionName)
                .Get<CorsOptions>()
                ?? new CorsOptions();

            options.AddPolicy(
                CorsOptions.PolicyName,
                policy =>
                {
                    policy
                        .WithOrigins(corsOptions.AllowedOrigins)
                        .WithMethods("GET", "POST")
                        .WithHeaders("Content-Type", "Accept");
                });
        });

        return services;
    }

    private static string ResolveErrorCode(
        ProblemDetails problem)
    {
        if (problem is HttpValidationProblemDetails)
        {
            return ErrorCodes.ValidationError;
        }

        return problem.Status switch
        {
            StatusCodes.Status400BadRequest =>
                ErrorCodes.InvalidRequest,

            StatusCodes.Status404NotFound =>
                ErrorCodes.NotFound,

            StatusCodes.Status415UnsupportedMediaType =>
                ErrorCodes.UnsupportedMediaType,

            StatusCodes.Status500InternalServerError =>
                ErrorCodes.InternalServerError,

            StatusCodes.Status503ServiceUnavailable =>
                ErrorCodes.DatabaseBusy,

            _ => "http_error"
        };
    }
}