using ATM.Application.Interfaces;
using ATM.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ATM.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(
        this IServiceCollection services)
    {
        services.AddScoped<IAtmService, AtmService>();

        return services;
    }
}