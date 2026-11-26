namespace ATM.Api.Configuration;

public sealed class CorsOptions
{
    public const string SectionName = "Cors";

    public const string PolicyName = "AtmClientPolicy";

    public string[] AllowedOrigins { get; init; } = [];
}