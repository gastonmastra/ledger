using Ledger.BuildingBlocks.Application;

namespace Ledger.Modules.Identity.Application.Users.GetOrCreateUser;

public sealed record GetOrCreateUserCommand(
    string ExternalId,
    string Email,
    string DisplayName) : ICommand<UserResult>;
