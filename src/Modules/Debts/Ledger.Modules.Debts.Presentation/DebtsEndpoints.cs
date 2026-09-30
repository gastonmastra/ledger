using Ledger.BuildingBlocks.Application;
using Ledger.Modules.Debts.Application.Operations;
using Ledger.Modules.Debts.Application.Queries;
using Ledger.Modules.Debts.Domain;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Ledger.Modules.Debts.Presentation;

public sealed record RecordLoanRequest(
    Guid ContactId,
    Guid WalletId,
    decimal Amount,
    DateTimeOffset? OccurredAt = null,
    string? Description = null);

public sealed record RecordSplitRequest(
    decimal Total,
    string Currency,
    SplitMethod Method,
    IReadOnlyList<SplitPayer> Payers,
    IReadOnlyList<SplitParticipant> Participants,
    Guid? GroupId = null,
    Guid? CategoryId = null,
    DateTimeOffset? OccurredAt = null,
    string? Description = null);

public sealed record RecordInitialDebtRequest(
    Guid ContactId,
    InitialDebtDirection Direction,
    decimal Amount,
    string Currency,
    DateTimeOffset? OccurredAt = null,
    string? Description = null);

/// <summary>
/// New values of a loan, existing debt, split or settlement (every field of its kind is sent again)
/// and why it changes. Loan: contactId, walletId, amount. Existing debt: contactId, direction,
/// amount, currency. Split: amount (total), currency, method, payers, participants, categoryId.
/// Settlement: walletId. Only whoever recorded it can change the contact, wallets, category or currency.
/// </summary>
public sealed record ReviseOperationRequest(
    string Reason,
    DateTimeOffset OccurredAt,
    string? Description = null,
    decimal? Amount = null,
    string? Currency = null,
    Guid? ContactId = null,
    InitialDebtDirection? Direction = null,
    Guid? WalletId = null,
    SplitMethod? Method = null,
    IReadOnlyList<SplitPayer>? Payers = null,
    IReadOnlyList<SplitParticipant>? Participants = null,
    Guid? CategoryId = null);

public sealed record OperationReasonRequest(string Reason);

public sealed record RespondToChangeRequest(string? Reason = null);

public sealed record SettleRequest(
    SettlementScope Scope,
    Guid? DebtId = null,
    Guid? ContactId = null,
    Guid? GroupId = null,
    Guid? FromPersonId = null,
    Guid? ToPersonId = null,
    string? Currency = null,
    Guid? WalletId = null,
    DateTimeOffset? OccurredAt = null,
    string? Description = null);

public static class DebtsEndpoints
{
    public static IEndpointRouteBuilder MapDebtsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var debts = endpoints
            .MapGroup("/api/debts")
            .WithTags("Debts")
            .RequireAuthorization();

        debts.MapGet("/", async (bool? includeSettled, ICurrentUser user, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new GetMyDebtsQuery(user.UserId, includeSettled ?? false), ct)))
            .WithSummary("What I am owed and what I owe, per debt and per contact");

        debts.MapPost("/loans", async (RecordLoanRequest request, ICurrentUser user, ISender sender, CancellationToken ct) =>
                Accepted(await sender.Send(
                    new RecordLoanCommand(
                        user.UserId, request.ContactId, request.WalletId, request.Amount,
                        request.OccurredAt ?? DateTimeOffset.UtcNow, request.Description),
                    ct)))
            .WithSummary("Lend money from one of my wallets to a contact (asynchronous)");

        debts.MapPost("/initial", async (RecordInitialDebtRequest request, ICurrentUser user, ISender sender, CancellationToken ct) =>
                Accepted(await sender.Send(
                    new RecordInitialDebtCommand(
                        user.UserId, request.ContactId, request.Direction, request.Amount, request.Currency,
                        request.OccurredAt ?? DateTimeOffset.UtcNow, request.Description),
                    ct)))
            .WithSummary("Record a debt that already existed before using Ledger; neither income nor expense (asynchronous)");

        debts.MapPost("/splits", async (RecordSplitRequest request, ICurrentUser user, ISender sender, CancellationToken ct) =>
                Accepted(await sender.Send(
                    new RecordSplitCommand(
                        user.UserId, request.GroupId, request.Total, request.Currency, request.Method,
                        request.Payers, request.Participants, request.CategoryId,
                        request.OccurredAt ?? DateTimeOffset.UtcNow, request.Description),
                    ct)))
            .WithSummary("Split an expense among people; any number of payers (asynchronous)");

        debts.MapPost("/settlements", async (SettleRequest request, ICurrentUser user, ISender sender, CancellationToken ct) =>
                Accepted(await sender.Send(
                    new SettleCommand(
                        user.UserId, request.Scope, request.DebtId, request.ContactId, request.GroupId,
                        request.FromPersonId, request.ToPersonId, request.Currency, request.WalletId,
                        request.OccurredAt ?? DateTimeOffset.UtcNow, request.Description),
                    ct)))
            .WithSummary("Fully settle a debt, everything with a contact, or a suggested group payment (asynchronous)");

        debts.MapGet("/operations", async (
                OperationKind? kind,
                Guid? groupId,
                Guid? personId,
                bool? includeHistory,
                int? page,
                int? pageSize,
                ICurrentUser user,
                ISender sender,
                CancellationToken ct) =>
                Results.Ok(await sender.Send(
                    new GetOperationsQuery(user.UserId, kind, groupId, personId, includeHistory ?? false, page ?? 1, pageSize ?? 30),
                    ct)))
            .WithSummary("Loans, existing debts, splits and settlements I am part of, newest first");

        debts.MapGet("/operations/{id:guid}", async (Guid id, ICurrentUser user, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new GetOperationQuery(user.UserId, id), ct)))
            .WithSummary("A loan, split or settlement with its status (Pending, Completed, Rejected, Superseded, Deleted)");

        debts.MapGet("/operations/{id:guid}/history", async (Guid id, ICurrentUser user, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new GetOperationHistoryQuery(user.UserId, id), ct)))
            .WithSummary("Every version of an operation and every change requested on it");

        debts.MapPut("/operations/{id:guid}", async (Guid id, ReviseOperationRequest request, ICurrentUser user, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(
                    new ReviseOperationCommand(
                        user.UserId, id, request.Reason, request.OccurredAt, request.Description, request.Amount,
                        request.Currency, request.ContactId, request.Direction, request.WalletId, request.Method,
                        request.Payers, request.Participants, request.CategoryId),
                    ct)))
            .WithSummary("Change an operation. Linked users involved must accept it first; then the books are updated (asynchronous)");

        debts.MapDelete("/operations/{id:guid}", async (Guid id, [FromBody] OperationReasonRequest request, ICurrentUser user, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new DeleteOperationCommand(user.UserId, id, request.Reason), ct)))
            .WithSummary("Delete an operation (deleting a settlement reopens its debts). Linked users must accept it first");

        debts.MapPost("/operations/{id:guid}/restore", async (Guid id, OperationReasonRequest request, ICurrentUser user, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new RestoreOperationCommand(user.UserId, id, request.Reason), ct)))
            .WithSummary("Bring back a deleted loan, existing debt or split. Linked users must accept it first");

        debts.MapGet("/changes", async (ChangesFilter? filter, ICurrentUser user, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new GetChangesQuery(user.UserId, filter ?? ChangesFilter.ToReview), ct)))
            .WithSummary("Changes of shared operations waiting for my answer (filter=All: every change I am part of)");

        debts.MapPost("/changes/{id:guid}/accept", async (Guid id, RespondToChangeRequest? request, ICurrentUser user, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new RespondToChangeCommand(user.UserId, id, true, request?.Reason), ct)))
            .WithSummary("Accept a change: my books get the new version");

        debts.MapPost("/changes/{id:guid}/reject", async (Guid id, RespondToChangeRequest? request, ICurrentUser user, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new RespondToChangeCommand(user.UserId, id, false, request?.Reason), ct)))
            .WithSummary("Reject a change: my books keep the previous version and the operation is marked as disputed");

        debts.MapGet("/activity", async (int? page, int? pageSize, ICurrentUser user, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new GetDebtActivityQuery(user.UserId, page ?? 1, pageSize ?? 30), ct)))
            .WithSummary("Recent changes of shared operations I requested or was asked about");

        endpoints.MapGet("/api/groups/{id:guid}/balances", async (Guid id, ICurrentUser user, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new GetGroupBalancesQuery(user.UserId, id), ct)))
            .WithTags("Groups")
            .RequireAuthorization()
            .WithSummary("Group balances, simplified suggested payments and raw debts");

        return endpoints;
    }

    private static IResult Accepted(OperationDto operation)
        => Results.Accepted($"/api/debts/operations/{operation.Id}", operation);
}
