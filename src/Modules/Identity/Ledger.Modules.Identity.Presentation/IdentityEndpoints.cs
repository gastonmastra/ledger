using Ledger.BuildingBlocks.Application;
using Ledger.Modules.Identity.Application.Users.GetUser;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Ledger.Modules.Identity.Presentation;

public static class IdentityEndpoints
{
    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/me", async (ICurrentUser user, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(new GetUserQuery(user.UserId), ct);

                // The person id of a registered user is its user id: use it as "me" in splits.
                return Results.Ok(new
                {
                    result.Id,
                    PersonId = result.Id,
                    result.Email,
                    result.DisplayName
                });
            })
            .RequireAuthorization()
            .WithTags("Identity")
            .WithName("GetMe")
            .WithSummary("Get the authenticated user");

        return endpoints;
    }
}
