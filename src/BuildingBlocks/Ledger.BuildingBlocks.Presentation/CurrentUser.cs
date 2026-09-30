using System.Security.Claims;
using Ledger.BuildingBlocks.Application;
using Microsoft.AspNetCore.Http;

namespace Ledger.BuildingBlocks.Presentation;

public sealed class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    /// <summary>
    /// Claim added by the Identity module once the token subject is mapped to an internal user.
    /// </summary>
    public const string UserIdClaim = "ledger_user_id";

    public Guid UserId
    {
        get
        {
            var value = httpContextAccessor.HttpContext?.User.FindFirstValue(UserIdClaim);

            return Guid.TryParse(value, out var userId)
                ? userId
                : throw new InvalidOperationException("The current request has no provisioned user.");
        }
    }
}
