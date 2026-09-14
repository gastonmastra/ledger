using System.Security.Claims;

namespace Ledger.Api.Identity;

public interface ICurrentUser
{
    string ExternalId { get; }
}

public sealed class CurrentUser(
    IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;

    public string ExternalId => 
        _httpContextAccessor.HttpContext?
            .User
            .FindFirstValue("sub")
            ?? throw new InvalidOperationException(
                "Authenticated user does not have a subject claim."
            );
}