using Ledger.BuildingBlocks.Application;
using Ledger.Modules.Wallets.Application;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Ledger.Modules.Wallets.Presentation;

public sealed record CreateWalletRequest(
    string Name,
    string Currency,
    decimal OpeningBalance = 0,
    DateTimeOffset? OpeningBalanceAt = null);

public sealed record RenameWalletRequest(string Name);

public static class WalletsEndpoints
{
    public static IEndpointRouteBuilder MapWalletsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/api/wallets")
            .WithTags("Wallets")
            .RequireAuthorization();

        group.MapGet("/", async (bool? includeArchived, ICurrentUser user, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new GetWalletsQuery(user.UserId, includeArchived ?? false), ct)))
            .WithSummary("List my wallets with their balances");

        group.MapGet("/{id:guid}", async (Guid id, ICurrentUser user, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new GetWalletQuery(user.UserId, id), ct)))
            .WithSummary("Get a wallet");

        group.MapPost("/", async (CreateWalletRequest request, ICurrentUser user, ISender sender, CancellationToken ct) =>
            {
                var wallet = await sender.Send(
                    new CreateWalletCommand(
                        user.UserId, request.Name, request.Currency, request.OpeningBalance, request.OpeningBalanceAt),
                    ct);

                return Results.Created($"/api/wallets/{wallet.Id}", wallet);
            })
            .WithSummary("Create a wallet, optionally with an opening balance and its date");

        group.MapPatch("/{id:guid}", async (Guid id, RenameWalletRequest request, ICurrentUser user, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new RenameWalletCommand(user.UserId, id, request.Name), ct);
                return Results.NoContent();
            })
            .WithSummary("Rename a wallet");

        group.MapPost("/{id:guid}/archive", async (Guid id, ICurrentUser user, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new ArchiveWalletCommand(user.UserId, id), ct);
                return Results.NoContent();
            })
            .WithSummary("Archive a wallet (balance must be zero)");

        group.MapDelete("/{id:guid}", async (Guid id, ICurrentUser user, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new DeleteWalletCommand(user.UserId, id), ct);
                return Results.NoContent();
            })
            .WithSummary("Delete a wallet (only when it has no transactions)");

        return endpoints;
    }
}
