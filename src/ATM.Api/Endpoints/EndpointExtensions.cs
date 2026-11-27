namespace ATM.Api.Endpoints;

public static class EndpointExtensions
{
    // Required on every money-moving POST. A retry with the same key returns
    // the original result instead of posting again.
    public const string IdempotencyKeyHeader = "Idempotency-Key";

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

    // OpenAPI metadata for the errors every money-moving endpoint can return.
    public static RouteHandlerBuilder ProducesMoneyOperationProblems(
        this RouteHandlerBuilder builder) => builder
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .ProducesProblem(StatusCodes.Status500InternalServerError)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
}