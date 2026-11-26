using ATM.Api.Contracts;
using ATM.Application.DTOs;
using ATM.Application.Interfaces;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ATM.Api.Endpoints;

public static class AccountEndpoints
{
    public static RouteGroupBuilder MapAccountEndpoints(
        this RouteGroupBuilder group)
    {
        group.WithTags("Accounts");

        group.MapGet("/", GetAccountsAsync)
            .WithName("GetAccounts")
            .WithSummary("Retrieve all ATM accounts");

        group.MapGet("/{id:guid}", GetAccountAsync)
            .WithName("GetAccount")
            .WithSummary("Retrieve an account by ID")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/deposits", DepositAsync)
            .WithName("Deposit")
            .WithSummary("Deposit funds into an account")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
            .ProducesProblem(StatusCodes.Status500InternalServerError);


        group.MapPost("/{id:guid}/withdrawals", WithdrawAsync)
            .WithName("Withdraw")
            .WithSummary("Withdraw funds from an account")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        group.MapGet("/{id:guid}/transactions", GetHistoryAsync)
            .WithName("GetTransactionHistory")
            .WithSummary("Retrieve account transaction history")
            .ProducesProblem(StatusCodes.Status404NotFound);

        return group;
    }

    private static async Task<Ok<IReadOnlyList<AccountDto>>>
        GetAccountsAsync(
            IAtmService service,
            CancellationToken cancellationToken)
    {
        var accounts = await service.GetAccountsAsync(
            cancellationToken);

        return TypedResults.Ok(accounts);
    }

    private static async Task<Ok<AccountDto>>
        GetAccountAsync(
            Guid id,
            IAtmService service,
            CancellationToken cancellationToken)
    {
        var account = await service.GetAccountAsync(
            id,
            cancellationToken);

        return TypedResults.Ok(account);
    }

    private static async Task<Ok<OperationResultDto>>
        DepositAsync(
            Guid id,
            AmountRequest request,
            IAtmService service,
            CancellationToken cancellationToken)
    {
        var result = await service.DepositAsync(
            id,
            new DepositRequest(request.Amount!.Value),
            cancellationToken);

        return TypedResults.Ok(result);
    }

    private static async Task<Ok<OperationResultDto>>
        WithdrawAsync(
            Guid id,
            AmountRequest request,
            IAtmService service,
            CancellationToken cancellationToken)
    {
        var result = await service.WithdrawAsync(
            id,
            new WithdrawalRequest(request.Amount!.Value),
            cancellationToken);

        return TypedResults.Ok(result);
    }

    private static async Task<Ok<IReadOnlyList<TransactionDto>>>
        GetHistoryAsync(
            Guid id,
            IAtmService service,
            CancellationToken cancellationToken)
    {
        var history = await service.GetTransactionHistoryAsync(
            id,
            cancellationToken);

        return TypedResults.Ok(history);
    }
}