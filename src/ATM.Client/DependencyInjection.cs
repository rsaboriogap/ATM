using System.Net.Http.Headers;
using ATM.Client.Configuration;
using ATM.Client.Interfaces;
using ATM.Client.Services;
using ATM.Client.State;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ATM.Client;

public static class DependencyInjection
{
    public static IServiceCollection AddClientServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection(
            ApiOptions.SectionName);

        var baseUrl = section["BaseUrl"]
            ?? throw new InvalidOperationException(
                "API BaseUrl is not configured.");

        var timeoutSeconds = 30;

        var configuredTimeout = section["TimeoutSeconds"];

        if (configuredTimeout is not null &&
            !int.TryParse(
                configuredTimeout,
                out timeoutSeconds))
        {
            throw new InvalidOperationException(
                "API timeout must be a valid integer.");
        }

        if (!Uri.TryCreate(
                baseUrl,
                UriKind.Absolute,
                out var baseAddress)
            || baseAddress.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(baseAddress.Query)
            || !string.IsNullOrEmpty(baseAddress.Fragment))
        {
            throw new InvalidOperationException(
                "API BaseUrl must be a valid HTTPS URL.");
        }

        if (timeoutSeconds is <= 0 or > 300)
        {
            throw new InvalidOperationException(
                "API timeout must be between 1 and 300 seconds.");
        }

        var options = new ApiOptions
        {
            BaseUrl = baseAddress.AbsoluteUri.TrimEnd('/') + "/",
            TimeoutSeconds = timeoutSeconds
        };

        services.AddSingleton(options);

        services.AddHttpClient<IAtmApiClient, AtmApiClient>(
            client =>
            {
                client.BaseAddress = new Uri(options.BaseUrl);

                client.Timeout = TimeSpan.FromSeconds(
                    options.TimeoutSeconds);

                client.DefaultRequestHeaders.Accept.Add(
                    new MediaTypeWithQualityHeaderValue(
                        "application/json"));

                client.DefaultRequestHeaders.Accept.Add(
                    new MediaTypeWithQualityHeaderValue(
                        "application/problem+json"));
            });

        services.AddScoped<AtmState>();

        return services;
    }
}