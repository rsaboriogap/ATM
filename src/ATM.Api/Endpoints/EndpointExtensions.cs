namespace ATM.Api.Endpoints;

public static class EndpointExtensions
{
    public static IEndpointRouteBuilder MapAtmEndpoints(
        this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapGroup("/accounts")
            .MapAccountEndpoints();

        api.MapGroup("/transfers")
            .MapTransferEndpoints();

        return app;
    }
}