namespace Ledger.Modules.Identity.Application.Users.GetOrCreateUser;

public sealed record UserResult(
    Guid Id,
    string ExternalId,
    string Email);