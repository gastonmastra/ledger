namespace Ledger.Modules.Identity.Contracts;

/// <summary>A person logged in for the first time and got an internal user id.</summary>
public sealed record UserRegistered(
    Guid UserId,
    string Email,
    string DisplayName);
