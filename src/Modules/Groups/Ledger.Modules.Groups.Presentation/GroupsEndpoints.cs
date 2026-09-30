using Ledger.BuildingBlocks.Application;
using Ledger.Modules.Groups.Application;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Ledger.Modules.Groups.Presentation;

public sealed record CreateGroupRequest(string Name, IReadOnlyList<Guid>? ContactIds = null);

public sealed record AddGroupMemberRequest(Guid ContactId);

public static class GroupsEndpoints
{
    public static IEndpointRouteBuilder MapGroupsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/api/groups")
            .WithTags("Groups")
            .RequireAuthorization();

        group.MapGet("/", async (ICurrentUser user, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new GetGroupsQuery(user.UserId), ct)))
            .WithSummary("List the groups I belong to");

        group.MapGet("/{id:guid}", async (Guid id, ICurrentUser user, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new GetGroupQuery(user.UserId, id), ct)))
            .WithSummary("Get a group and its members");

        group.MapPost("/", async (CreateGroupRequest request, ICurrentUser user, ISender sender, CancellationToken ct) =>
            {
                var created = await sender.Send(
                    new CreateGroupCommand(user.UserId, request.Name, request.ContactIds ?? []), ct);

                return Results.Created($"/api/groups/{created.Id}", created);
            })
            .WithSummary("Create a group with me and some of my contacts");

        group.MapPost("/{id:guid}/members", async (Guid id, AddGroupMemberRequest request, ICurrentUser user, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new AddGroupMemberCommand(user.UserId, id, request.ContactId), ct)))
            .WithSummary("Add one of my contacts to the group");

        group.MapDelete("/{id:guid}/members/{personId:guid}", async (Guid id, Guid personId, ICurrentUser user, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new RemoveGroupMemberCommand(user.UserId, id, personId), ct);
                return Results.NoContent();
            })
            .WithSummary("Remove a member without group activity");

        return endpoints;
    }
}
