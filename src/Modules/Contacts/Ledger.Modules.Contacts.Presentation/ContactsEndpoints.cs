using Ledger.BuildingBlocks.Application;
using Ledger.Modules.Contacts.Application;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Ledger.Modules.Contacts.Presentation;

public sealed record CreateContactRequest(string Name, string? Email = null);

public sealed record LinkContactRequest(string Email);

public static class ContactsEndpoints
{
    public static IEndpointRouteBuilder MapContactsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/api/contacts")
            .WithTags("Contacts")
            .RequireAuthorization();

        group.MapGet("/", async (ICurrentUser user, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new GetContactsQuery(user.UserId), ct)))
            .WithSummary("List my contacts");

        group.MapPost("/", async (CreateContactRequest request, ICurrentUser user, ISender sender, CancellationToken ct) =>
            {
                var contact = await sender.Send(new CreateContactCommand(user.UserId, request.Name, request.Email), ct);

                return Results.Created($"/api/contacts/{contact.Id}", contact);
            })
            .WithSummary("Add a contact; it is linked to the registered user with that email, if any");

        group.MapPost("/{id:guid}/link", async (Guid id, LinkContactRequest request, ICurrentUser user, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new LinkContactCommand(user.UserId, id, request.Email), ct)))
            .WithSummary("Link a contact (not used yet) to a registered user");

        return endpoints;
    }
}
