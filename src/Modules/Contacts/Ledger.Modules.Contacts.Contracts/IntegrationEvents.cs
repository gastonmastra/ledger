namespace Ledger.Modules.Contacts.Contracts;

/// <param name="PersonId">Global id of the person: the linked user id, or an id minted for a non-registered person.</param>
public sealed record ContactCreated(
    Guid ContactId,
    Guid OwnerId,
    Guid PersonId,
    string Name,
    string? Email,
    Guid? LinkedUserId);

/// <summary>A contact that was not used yet was linked to a registered user; its person id changed.</summary>
public sealed record ContactLinked(
    Guid ContactId,
    Guid OwnerId,
    Guid PreviousPersonId,
    Guid PersonId,
    Guid LinkedUserId,
    string Name);
