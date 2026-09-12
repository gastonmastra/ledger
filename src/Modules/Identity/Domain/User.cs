namespace Ledger.Modules.Identity.Domain;

public sealed class User
{
    private User(
        Guid id,
        string externalId,
        string email
    )
    {
        Id = id;
        ExternalId = externalId;
        Email = email;
        CreatedAt = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public string ExternalId { get; private set; }
    public string Email { get; private set; }
    public DateTime CreatedAt { get; private set; }
    
    public static User Create(
        string externalId,
        string email
    )
    {
        if (string.IsNullOrWhiteSpace(externalId))
            throw new ArgumentException("External ID is required.");

        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email is required.");
        
        return new User(
            Guid.NewGuid(),
            externalId,
            email);
    }
}