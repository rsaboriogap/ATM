using ATM.Api.Configuration;
using ATM.Api.Endpoints;
using ATM.Application;
using ATM.Infrastructure;
using ATM.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// API services
builder.Services.AddApiServices(
    builder.Configuration);

// Application services
builder.Services.AddApplication();

// Infrastructure services
builder.Services.AddInfrastructure(
    builder.Configuration);

// Persist logs to the Logs table (levels: Logging:Database)
//builder.Logging.AddDatabaseLogger();

var app = builder.Build();

// Global HTTP error handling
app.UseExceptionHandler();

app.UseStatusCodePages();

// HTTPS
app.UseHttpsRedirection();

// Routing
app.UseRouting();

// Cross-origin requests
app.UseCors(CorsOptions.PolicyName);

// Development documentation
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint(
            "/openapi/v1.json",
            "ATM API v1");

        options.RoutePrefix = "swagger";
        options.DocumentTitle = "ATM API Documentation";
        options.DisplayRequestDuration();
        options.EnableTryItOutByDefault();
    });
}

// Initialize database
await using (var scope = app.Services.CreateAsyncScope())
{
    var initializer = scope.ServiceProvider
        .GetRequiredService<DatabaseInitializer>();

    await initializer.InitializeAsync();
}

// REST endpoints
app.MapAtmEndpoints();

app.Run();

public partial class Program
{
}