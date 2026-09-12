using System.Security.Claims;
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

        group.MapGet("/", GetWallets);
        group.MapPost("/", CreateWallet);

        return endpoints;
    }

    private static async Task<IResult> GetWallets(
        ClaimsPrincipal user,
        GetWalletsHandler handler,
        CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(
            user.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var query = new GetWalletsQuery(userId);
        var wallets = await handler.Handle(query, cancellationToken);

        return Results.Ok(wallets);   
    }

    private static async Task<IResult> CreateWallet(
        ClaimsPrincipal user,
        CreateWalletRequest request,
        CreateWalletHandler handler,
        CancellationToken cancellationToken)
    {
        // var userId = Guid.Parse(
        //     user.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var command = new CreateWalletCommand(
            UserId: Guid.NewGuid(),
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