using Ledger.BuildingBlocks.Domain;

namespace Ledger.Modules.Identity.Domain;

/// <summary>A person authenticated by the external identity provider (Keycloak).</summary>
public sealed class User
{
    private User(
        Guid id,
        string externalId,
        string email,
        string displayName)
    {
        Id = id;
        ExternalId = externalId;
        Email = email;
        DisplayName = displayName;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public string ExternalId { get; private set; }
    public string Email { get; private set; }
    public string DisplayName { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static User Create(
        string externalId,
        string email,
        string displayName)
    {
        if (string.IsNullOrWhiteSpace(externalId))
            throw new DomainException("user.external_id_required", "External ID is required.");

        if (string.IsNullOrWhiteSpace(email))
            throw new DomainException("user.email_required", "Email is required.");

        return new User(
            Guid.NewGuid(),
            externalId,
            email.Trim().ToLowerInvariant(),
            string.IsNullOrWhiteSpace(displayName) ? email : displayName.Trim());
    }
}
