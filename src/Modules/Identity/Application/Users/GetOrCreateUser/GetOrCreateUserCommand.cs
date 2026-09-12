namespace Ledger.Modules.Identity.Application.Users.GetOrCreateUser;

public sealed record GetOrCreateUserCommand(
    string ExternalId,
    string Email);