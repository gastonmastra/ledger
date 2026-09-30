using System.Security.Claims;
using Ledger.BuildingBlocks.Presentation;
using Ledger.Modules.Identity.Application.Users.GetOrCreateUser;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;

namespace Ledger.Modules.Identity.Presentation;

/// <summary>
/// Maps the Keycloak subject of every authenticated request to the internal user id
/// (creating the user on first login) and exposes it as the <see cref="CurrentUser.UserIdClaim"/> claim.
/// </summary>
public sealed class UserProvisioningMiddleware(RequestDelegate next)
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);

    public async Task InvokeAsync(HttpContext context, ISender sender, IMemoryCache cache)
    {
        var principal = context.User;
        var subject = principal.FindFirstValue("sub");

        if (principal.Identity?.IsAuthenticated == true && subject is not null)
        {
            var userId = await cache.GetOrCreateAsync($"identity:user:{subject}", async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = CacheDuration;

                var email = principal.FindFirstValue("email")
                    ?? $"{subject}@users.ledger.local";
                var displayName = principal.FindFirstValue("name")
                    ?? principal.FindFirstValue("preferred_username")
                    ?? email;

                var user = await sender.Send(
                    new GetOrCreateUserCommand(subject, email, displayName),
                    context.RequestAborted);

                return user.Id;
            });

            principal.AddIdentity(new ClaimsIdentity(
                [new Claim(CurrentUser.UserIdClaim, userId.ToString())]));
        }

        await next(context);
    }
}

public static class UserProvisioningMiddlewareExtensions
{
    public static IApplicationBuilder UseUserProvisioning(this IApplicationBuilder app)
        => app.UseMiddleware<UserProvisioningMiddleware>();
}
