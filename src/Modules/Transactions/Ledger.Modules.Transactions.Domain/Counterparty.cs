namespace Ledger.Modules.Transactions.Domain;

public enum CounterpartyKind
{
    Contact,
    Group
}

/// <summary>
/// Name of a counterparty (a contact or a group) as seen by one user; used to show and filter
/// receivable/payable postings.
/// </summary>
public sealed class Counterparty
{
    private Counterparty()
    {
        Name = null!;
    }

    public Guid OwnerId { get; private set; }
    public Guid PersonId { get; private set; }
    public CounterpartyKind Kind { get; private set; }

    /// <summary>Contact id (for contacts) or group id (for groups) in the owner's lists.</summary>
    public Guid SourceId { get; private set; }

    public string Name { get; private set; }

    public static Counterparty Create(Guid ownerId, Guid personId, CounterpartyKind kind, Guid sourceId, string name)
        => new()
        {
            OwnerId = ownerId,
            PersonId = personId,
            Kind = kind,
            SourceId = sourceId,
            Name = name
        };

    public void Rename(string name) => Name = name;
}
