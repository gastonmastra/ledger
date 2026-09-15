namespace Ledger.Modules.Transactions.Api;

using Ledger.Api.Transactions;
using Ledger.Modules.Transactions.Application.CreateTransaction;
using Ledger.Modules.Transactions.Application.GetTransaction;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

public static class TransactionEndpoints
{
    public static IEndpointRouteBuilder MapTransactionEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/api/transactions")
            .WithTags("Transactions");

        group.MapPost(
            "/",
            async (
                CreateTransactionRequest request,
                CreateTransactionHandler handler,
                CancellationToken cancellationToken) =>
            {
                var command = new CreateTransactionCommand(
                    request.OccurredAt,
                    request.Description,
                    request.Postings
                        .Select(x =>
                            new CreateTransactionPosting(
                                x.AccountId,
                                x.Amount))
                        .ToList());

                var id = await handler.Handle(
                    command,
                    cancellationToken);

                return Results.Created(
                    $"/api/transactions/{id.Value}",
                    new { id = id.Value });
            });

        group.MapGet(
            "/{id:guid}",
            async (
                Guid id,
                GetTransactionHandler handler,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.Handle(
                    new GetTransactionQuery(id),
                    cancellationToken);

                return result is null
                    ? Results.NotFound()
                    : Results.Ok(result);
            });

        return endpoints;
    }

}