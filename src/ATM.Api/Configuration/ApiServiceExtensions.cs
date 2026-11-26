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

        services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = context =>
            {
                var problem = context.ProblemDetails;

                problem.Instance ??=
                    context.HttpContext.Request.Path.Value;

                if (!problem.Extensions.ContainsKey("traceId"))
                {
                    problem.Extensions["traceId"] =
                        Activity.Current?.Id
                        ?? context.HttpContext.TraceIdentifier;
                }

                if (!problem.Extensions.ContainsKey("code"))
                {
                    problem.Extensions["code"] =
                        ResolveErrorCode(problem);
                }
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