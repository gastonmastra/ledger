using Ledger.BuildingBlocks.Application;
using Ledger.Modules.Transactions.Application.Ledger;
using Ledger.Modules.Transactions.Application.Queries;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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

public sealed record ChangeOpeningBalanceRequest(Guid WalletId, decimal Amount, string Reason, DateTimeOffset? OccurredAt = null);

/// <summary>New values of an income, expense or transfer (every field is sent again) and why it changes.</summary>
/// <param name="WalletId">The wallet (for a transfer: the source wallet).</param>
public sealed record ReviseTransactionRequest(
    Guid WalletId,
    decimal Amount,
    DateTimeOffset OccurredAt,
    string Reason,
    Guid? ToWalletId = null,
    decimal? ReceivedAmount = null,
    Guid? CategoryId = null,
    string? Description = null);

public sealed record ReasonRequest(string Reason);

public sealed record BooksLockRequest(DateTimeOffset? LockedUntil);

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
                bool? includeHistory,
                ICurrentUser user,
                ISender sender,
                CancellationToken ct) =>
                Results.Ok(await sender.Send(
                    new GetTransactionsQuery(
                        user.UserId, page ?? 1, pageSize ?? 20, walletId, from, to, contactId, categoryId,
                        includeHistory ?? false),
                    ct)))
            .WithSummary("List my transactions, newest first, with optional filters (includeHistory: also changed, deleted and reversal entries)");

        transactions.MapGet("/activity", async (int? page, int? pageSize, ICurrentUser user, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new GetTransactionActivityQuery(user.UserId, page ?? 1, pageSize ?? 30), ct)))
            .WithSummary("My recent edits, deletions and restores of income, expenses, transfers and balances");

        transactions.MapGet("/{id:guid}", async (Guid id, ICurrentUser user, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new GetTransactionQuery(user.UserId, id), ct)))
            .WithSummary("Get a transaction and its postings");

        transactions.MapGet("/{id:guid}/history", async (Guid id, ICurrentUser user, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new GetTransactionHistoryQuery(user.UserId, id), ct)))
            .WithSummary("Every version of a transaction: who changed it, when and why");

        transactions.MapPut("/{id:guid}", async (Guid id, ReviseTransactionRequest request, ICurrentUser user, ISender sender, CancellationToken ct) =>
            {
                var newId = await sender.Send(
                    new ReviseMovementCommand(
                        user.UserId, id, request.WalletId, request.ToWalletId, request.CategoryId, request.Amount,
                        request.ReceivedAmount, request.OccurredAt, request.Description, request.Reason),
                    ct);

                return Results.Ok(new { id = newId });
            })
            .WithSummary("Correct an income, expense or transfer: the current version is reversed and the corrected one recorded");

        transactions.MapDelete("/{id:guid}", async (Guid id, [FromBody] ReasonRequest request, ICurrentUser user, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new DeleteTransactionCommand(user.UserId, id, request.Reason), ct);
                return Results.NoContent();
            })
            .WithSummary("Delete an income, expense, transfer or balance adjustment (a reversal is recorded; it can be restored)");

        transactions.MapPost("/{id:guid}/restore", async (Guid id, ReasonRequest request, ICurrentUser user, ISender sender, CancellationToken ct) =>
            {
                var newId = await sender.Send(new RestoreTransactionCommand(user.UserId, id, request.Reason), ct);
                return Results.Ok(new { id = newId });
            })
            .WithSummary("Bring back a deleted transaction");

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
                        user.UserId, request.WalletId, request.Amount, request.OccurredAt ?? DateTimeOffset.UtcNow,
                        request.Reason),
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

        var settings = endpoints
            .MapGroup("/api/settings")
            .WithTags("Settings")
            .RequireAuthorization();

        settings.MapGet("/books-lock", async (ICurrentUser user, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new GetBooksLockQuery(user.UserId), ct)))
            .WithSummary("The date up to which my books are closed (null: nothing is locked)");

        settings.MapPut("/books-lock", async (BooksLockRequest request, ICurrentUser user, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new SetBooksLockCommand(user.UserId, request.LockedUntil), ct)))
            .WithSummary("Close my books up to a date: nothing on or before it can be recorded or changed. Null reopens everything");

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
