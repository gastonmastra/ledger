using Ledger.BuildingBlocks.Application;
using Ledger.Modules.Debts.Application.Operations;
using Ledger.Modules.Debts.Application.Queries;
using Ledger.Modules.Debts.Domain;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
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

        debts.MapGet("/operations/{id:guid}", async (Guid id, ICurrentUser user, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new GetOperationQuery(user.UserId, id), ct)))
            .WithSummary("Status of a loan, split or settlement: Pending, Completed or Rejected");

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
