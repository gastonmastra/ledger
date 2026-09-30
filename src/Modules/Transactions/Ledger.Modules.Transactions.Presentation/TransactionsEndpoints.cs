using Ledger.BuildingBlocks.Application;
using Ledger.Modules.Transactions.Application.Ledger;
using Ledger.Modules.Transactions.Application.Queries;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Ledger.Modules.Transactions.Presentation;

public sealed record RecordIncomeRequest(
    Guid WalletId,
    decimal Amount,
    Guid? CategoryId = null,
    DateTimeOffset? OccurredAt = null,
    string? Description = null);

public sealed record RecordExpenseRequest(
    Guid WalletId,
    decimal Amount,
    Guid? CategoryId = null,
    DateTimeOffset? OccurredAt = null,
    string? Description = null);

public sealed record TransferRequest(
    Guid FromWalletId,
    Guid ToWalletId,
    decimal Amount,
    decimal? ReceivedAmount = null,
    DateTimeOffset? OccurredAt = null,
    string? Description = null);

public sealed record AssignWalletRequest(Guid WalletId, DateTimeOffset? OccurredAt = null);

public sealed record ChangeOpeningBalanceRequest(Guid WalletId, decimal Amount, DateTimeOffset? OccurredAt = null);

public sealed record AdjustBalanceRequest(
    Guid WalletId,
    decimal ActualBalance,
    DateTimeOffset? OccurredAt = null,
    string? Description = null);

public static class TransactionsEndpoints
{
    public static IEndpointRouteBuilder MapTransactionsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var transactions = endpoints
            .MapGroup("/api/transactions")
            .WithTags("Transactions")
            .RequireAuthorization();

        transactions.MapGet("/", async (
                int? page,
                int? pageSize,
                Guid? walletId,
                DateTimeOffset? from,
                DateTimeOffset? to,
                Guid? contactId,
                Guid? categoryId,
                ICurrentUser user,
                ISender sender,
                CancellationToken ct) =>
                Results.Ok(await sender.Send(
                    new GetTransactionsQuery(
                        user.UserId, page ?? 1, pageSize ?? 20, walletId, from, to, contactId, categoryId),
                    ct)))
            .WithSummary("List my transactions, newest first, with optional filters");

        transactions.MapGet("/{id:guid}", async (Guid id, ICurrentUser user, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new GetTransactionQuery(user.UserId, id), ct)))
            .WithSummary("Get a transaction and its postings");

        transactions.MapPost("/income", async (RecordIncomeRequest request, ICurrentUser user, ISender sender, CancellationToken ct) =>
            {
                var id = await sender.Send(
                    new RecordIncomeCommand(
                        user.UserId, request.WalletId, request.CategoryId, request.Amount,
                        request.OccurredAt ?? DateTimeOffset.UtcNow, request.Description),
                    ct);

                return Results.Created($"/api/transactions/{id}", new { id });
            })
            .WithSummary("Record money coming into a wallet");

        transactions.MapPost("/expense", async (RecordExpenseRequest request, ICurrentUser user, ISender sender, CancellationToken ct) =>
            {
                var id = await sender.Send(
                    new RecordExpenseCommand(
                        user.UserId, request.WalletId, request.CategoryId, request.Amount,
                        request.OccurredAt ?? DateTimeOffset.UtcNow, request.Description),
                    ct);

                return Results.Created($"/api/transactions/{id}", new { id });
            })
            .WithSummary("Record a purchase paid from a wallet");

        transactions.MapPost("/transfer", async (TransferRequest request, ICurrentUser user, ISender sender, CancellationToken ct) =>
            {
                var id = await sender.Send(
                    new TransferCommand(
                        user.UserId, request.FromWalletId, request.ToWalletId, request.Amount, request.ReceivedAmount,
                        request.OccurredAt ?? DateTimeOffset.UtcNow, request.Description),
                    ct);

                return Results.Created($"/api/transactions/{id}", new { id });
            })
            .WithSummary("Move money between two of my wallets (receivedAmount is required across currencies)");

        transactions.MapGet("/opening-balance/{walletId:guid}", async (Guid walletId, ICurrentUser user, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new GetOpeningBalanceQuery(user.UserId, walletId), ct)))
            .WithSummary("The current opening balance of a wallet (amount and date)");

        transactions.MapPut("/opening-balance", async (ChangeOpeningBalanceRequest request, ICurrentUser user, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(
                    new ChangeOpeningBalanceCommand(
                        user.UserId, request.WalletId, request.Amount, request.OccurredAt ?? DateTimeOffset.UtcNow),
                    ct);

                return Results.NoContent();
            })
            .WithSummary("Replace the opening balance of a wallet (the old one is reversed, never edited)");

        transactions.MapPost("/balance-adjustment", async (AdjustBalanceRequest request, ICurrentUser user, ISender sender, CancellationToken ct) =>
            {
                var id = await sender.Send(
                    new AdjustBalanceCommand(
                        user.UserId, request.WalletId, request.ActualBalance,
                        request.OccurredAt ?? DateTimeOffset.UtcNow, request.Description),
                    ct);

                return Results.Created($"/api/transactions/{id}", new { id });
            })
            .WithSummary("Set a wallet to its real balance; the difference is recorded as a balance adjustment");

        endpoints.MapGet("/api/reports", async (
                DateTimeOffset from,
                DateTimeOffset to,
                string? currency,
                string? timeZone,
                ICurrentUser user,
                ISender sender,
                CancellationToken ct) =>
                Results.Ok(await sender.Send(new GetReportQuery(user.UserId, from, to, currency, timeZone), ct)))
            .WithTags("Reports")
            .RequireAuthorization()
            .WithSummary("Figures for charts: income vs expenses, spending by category, net worth and wallet balances");

        endpoints.MapGet("/api/summary", async (ICurrentUser user, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new GetSummaryQuery(user.UserId), ct)))
            .WithTags("Summary")
            .RequireAuthorization()
            .WithSummary("Totals per currency: wallets, owed to me, I owe, pending and net");

        var pending = endpoints
            .MapGroup("/api/pending-assignments")
            .WithTags("Pending assignments")
            .RequireAuthorization();

        pending.MapGet("/", async (ICurrentUser user, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new GetPendingAssignmentsQuery(user.UserId), ct)))
            .WithSummary("Money recorded by others on my behalf that needs a wallet");

        pending.MapPost("/{id:guid}/assign", async (Guid id, AssignWalletRequest request, ICurrentUser user, ISender sender, CancellationToken ct) =>
            {
                var transactionId = await sender.Send(
                    new AssignWalletCommand(user.UserId, id, request.WalletId, request.OccurredAt), ct);

                return Results.Ok(new { transactionId });
            })
            .WithSummary("Choose the wallet the money came from or went to");

        return endpoints;
    }
}
