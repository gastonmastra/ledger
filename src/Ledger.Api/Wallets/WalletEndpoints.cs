using System.Security.Claims;
using Ledger.Api.Identity;
using Ledger.Modules.Wallets.Application.CreateWallet;
using Ledger.Modules.Wallets.Application.GetWallets;

namespace Ledger.Api.Wallets;

public static class WalletsEndpoints
{
    public static IEndpointRouteBuilder MapWalletsEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/api/wallets")
            .WithTags("Wallets");

        group.MapGet("/", GetWallets).RequireAuthorization();
        group.MapPost("/", CreateWallet).RequireAuthorization();

        return endpoints;
    }

    private static async Task<IResult> GetWallets(
        ICurrentUser user,
        ClaimsPrincipal user2,
        GetWalletsHandler handler,
        CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(user.ExternalId);
        var query = new GetWalletsQuery(userId);
        var wallets = await handler.Handle(query, cancellationToken);

        return Results.Ok(wallets);   
    }

    private static async Task<IResult> CreateWallet(
        ICurrentUser user,
        CreateWalletRequest request,
        CreateWalletHandler handler,
        CancellationToken cancellationToken)
    {
        var command = new CreateWalletCommand(
            UserId: Guid.Parse(user.ExternalId),
            Name: request.Name,
            Currency: request.Currency
        );
        var wallet = await handler.Handle(command, cancellationToken);
        var response = new CreateWalletResponse(
            Id: wallet.Id,
            Name: wallet.Name,
            Currency: wallet.Currency
        );

        return Results.Created(
            $"/api/wallets/{wallet.Id}", 
            response);   
    }
}