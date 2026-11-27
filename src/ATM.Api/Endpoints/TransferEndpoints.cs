using ATM.Api.Contracts;
using ATM.Application.DTOs;
using ATM.Application.Interfaces;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ATM.Api.Endpoints;

public static class TransferEndpoints
{
    public static RouteGroupBuilder MapTransferEndpoints(
        this RouteGroupBuilder group)
    {
        group.WithTags("Transfers");

        group.MapPost("/", TransferAsync)
            .WithName("Transfer")
            .WithSummary("Transfer funds between accounts")
            .ProducesMoneyOperationProblems();

        return group;
    }

    private static async Task<Ok<TransferResultDto>>
        TransferAsync(
            TransferApiRequest request,
            IAtmService service,
            CancellationToken cancellationToken)
    {
        var applicationRequest = new TransferRequest(
            request.SourceAccountId!.Value,
            request.DestinationAccountId!.Value,
            request.Amount!.Value);

        var result = await service.TransferAsync(
            applicationRequest,
            cancellationToken);

        return TypedResults.Ok(result);
    }
}