using Ledger.BuildingBlocks.Application;
using Ledger.Modules.Investments.Application;
using Ledger.Modules.Investments.Domain;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Ledger.Modules.Investments.Presentation;

public sealed record CreateAssetRequest(string Name, string? Symbol, AssetType Type, string Currency);

public sealed record UpdateAssetRequest(string Name, string? Symbol);

public sealed record RecordPriceRequest(decimal? BuyPrice, decimal? SellPrice, DateTimeOffset? OccurredAt = null);

/// <param name="WalletAmount">Money in the wallet's currency; required when it differs from the asset currency.</param>
/// <param name="SellPrice">Optional current sell price of the asset, recorded with the purchase.</param>
public sealed record BuyRequest(
    Guid AssetId,
    decimal Quantity,
    decimal UnitPrice,
    Guid WalletId,
    decimal? WalletAmount = null,
    decimal? SellPrice = null,
    DateTimeOffset? OccurredAt = null,
    string? Description = null);

/// <param name="WalletAmount">Money received in the wallet's currency; required when it differs from the asset currency.</param>
/// <param name="BuyPrice">Optional current buy price of the asset, recorded with the sale.</param>
public sealed record SellRequest(
    Guid AssetId,
    decimal Quantity,
    decimal UnitPrice,
    Guid WalletId,
    decimal? WalletAmount = null,
    decimal? BuyPrice = null,
    DateTimeOffset? OccurredAt = null,
    string? Description = null);

/// <param name="UnitPrice">What each unit cost.</param>
/// <param name="SellPrice">Optional current sell price of the asset.</param>
public sealed record OpeningHoldingRequest(
    Guid AssetId,
    decimal Quantity,
    decimal UnitPrice,
    decimal? SellPrice = null,
    DateTimeOffset? OccurredAt = null,
    string? Description = null);

public static class InvestmentsEndpoints
{
    public static IEndpointRouteBuilder MapInvestmentsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/api/investments")
            .WithTags("Investments")
            .RequireAuthorization();

        group.MapGet("/assets", async (bool? includeArchived, ICurrentUser user, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new GetAssetsQuery(user.UserId, includeArchived ?? false), ct)))
            .WithSummary("My assets with their holdings, valued at the latest sell price (else buy price, else cost)");

        group.MapGet("/assets/{id:guid}", async (Guid id, ICurrentUser user, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new GetAssetQuery(user.UserId, id), ct)))
            .WithSummary("Get an asset and its holding");

        group.MapPost("/assets", async (CreateAssetRequest request, ICurrentUser user, ISender sender, CancellationToken ct) =>
            {
                var asset = await sender.Send(
                    new CreateAssetCommand(user.UserId, request.Name, request.Symbol, request.Type, request.Currency), ct);

                return Results.Created($"/api/investments/assets/{asset.Id}", asset);
            })
            .WithSummary("Add an asset (share, ETF, crypto…) quoted in a currency");

        group.MapPut("/assets/{id:guid}", async (Guid id, UpdateAssetRequest request, ICurrentUser user, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new UpdateAssetCommand(user.UserId, id, request.Name, request.Symbol), ct);
                return Results.NoContent();
            })
            .WithSummary("Rename an asset or change its symbol");

        group.MapPost("/assets/{id:guid}/archive", async (Guid id, ICurrentUser user, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new ArchiveAssetCommand(user.UserId, id), ct);
                return Results.NoContent();
            })
            .WithSummary("Archive an asset you no longer hold");

        group.MapGet("/assets/{id:guid}/prices", async (Guid id, ICurrentUser user, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new GetAssetPricesQuery(user.UserId, id), ct)))
            .WithSummary("Known buy and sell prices of an asset, newest first");

        group.MapPost("/assets/{id:guid}/prices", async (Guid id, RecordPriceRequest request, ICurrentUser user, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(
                    new RecordPriceCommand(
                        user.UserId, id, request.BuyPrice, request.SellPrice, request.OccurredAt ?? DateTimeOffset.UtcNow),
                    ct);
                return Results.NoContent();
            })
            .WithSummary("Record the buy and/or sell price of an asset (no money moves)");

        group.MapPost("/trades/buy", async (BuyRequest request, ICurrentUser user, ISender sender, CancellationToken ct) =>
                Accepted(await sender.Send(
                    new RecordTradeCommand(
                        user.UserId, TradeKind.Buy, request.AssetId, request.Quantity, request.UnitPrice, request.WalletId,
                        request.WalletAmount, request.SellPrice, request.OccurredAt ?? DateTimeOffset.UtcNow, request.Description),
                    ct)))
            .WithSummary("Buy an asset with money of a wallet (asynchronous: poll the trade)");

        group.MapPost("/trades/sell", async (SellRequest request, ICurrentUser user, ISender sender, CancellationToken ct) =>
                Accepted(await sender.Send(
                    new RecordTradeCommand(
                        user.UserId, TradeKind.Sell, request.AssetId, request.Quantity, request.UnitPrice, request.WalletId,
                        request.WalletAmount, request.BuyPrice, request.OccurredAt ?? DateTimeOffset.UtcNow, request.Description),
                    ct)))
            .WithSummary("Sell (part of) a holding into a wallet (asynchronous: poll the trade)");

        group.MapPost("/trades/opening", async (OpeningHoldingRequest request, ICurrentUser user, ISender sender, CancellationToken ct) =>
                Accepted(await sender.Send(
                    new RecordTradeCommand(
                        user.UserId, TradeKind.Opening, request.AssetId, request.Quantity, request.UnitPrice, null,
                        null, request.SellPrice, request.OccurredAt ?? DateTimeOffset.UtcNow, request.Description),
                    ct)))
            .WithSummary("Record a holding you already owned before using Ledger (asynchronous: poll the trade)");

        group.MapGet("/trades", async (Guid? assetId, int? page, int? pageSize, ICurrentUser user, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new GetTradesQuery(user.UserId, assetId, page ?? 1, pageSize ?? 50), ct)))
            .WithSummary("My trades, newest first");

        group.MapGet("/trades/{id:guid}", async (Guid id, ICurrentUser user, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new GetTradeQuery(user.UserId, id), ct)))
            .WithSummary("Get a trade (Pending, Completed or Rejected with a reason)");

        group.MapGet("/summary", async (ICurrentUser user, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new GetInvestmentsSummaryQuery(user.UserId), ct)))
            .WithSummary("Market value, cost, unrealized and realized gains per currency");

        group.MapGet("/report", async (DateTimeOffset from, DateTimeOffset to, string? currency, string? timeZone, ICurrentUser user, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new GetInvestmentsReportQuery(user.UserId, from, to, currency, timeZone), ct)))
            .WithSummary("Value of the holdings over time (same buckets as /api/reports) and per asset");

        return endpoints;
    }

    private static IResult Accepted(TradeDto trade)
        => Results.Accepted($"/api/investments/trades/{trade.Id}", trade);
}
