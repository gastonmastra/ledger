namespace Ledger.Modules.Identity.Application.Users;

public sealed record UserResult(
    Guid Id,
    string ExternalId,
    string Email,
    string DisplayName);
