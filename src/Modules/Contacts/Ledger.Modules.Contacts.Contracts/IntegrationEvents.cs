namespace Ledger.Modules.Contacts.Contracts;

/// <param name="PersonId">Global id of the person: the linked user id, or an id minted for a non-registered person.</param>
public sealed record ContactCreated(
    Guid ContactId,
    Guid OwnerId,
    Guid PersonId,
    string Name,
    string? Email,
    Guid? LinkedUserId);

/// <summary>A contact was linked to a registered user; its person id changed.</summary>
/// <param name="MergedIntoContactId">The owner already had a contact for that user: this contact was merged into it
/// (it keeps its previous person id in Contacts, is hidden, and its history now belongs to the other contact).</param>
public sealed record ContactLinked(
    Guid ContactId,
    Guid OwnerId,
    Guid PreviousPersonId,
    Guid PersonId,
    Guid LinkedUserId,
    string Name,
    Guid? MergedIntoContactId = null);

/// <summary>
/// The owner asked to link a contact that already takes part in debts or groups to a registered user.
/// Nothing changes until that user accepts.
/// </summary>
public sealed record ContactLinkRequested(
    Guid RequestId,
    Guid OwnerId,
    Guid ContactId,
    Guid PersonId,
    Guid TargetUserId);

/// <summary>The link request was cancelled, rejected or failed: the preview is no longer needed.</summary>
public sealed record ContactLinkRequestClosed(Guid RequestId);

/// <summary>
/// The user accepted the link: the person <c>PreviousPersonId</c> becomes the user <c>UserId</c> everywhere, and
/// the shared history is copied into the user's books. Answered with <c>PersonMerged</c> or <c>PersonMergeFailed</c>.
/// </summary>
public sealed record PersonMergeRequested(
    Guid RequestId,
    Guid OwnerId,
    Guid ContactId,
    Guid PreviousPersonId,
    Guid UserId);
