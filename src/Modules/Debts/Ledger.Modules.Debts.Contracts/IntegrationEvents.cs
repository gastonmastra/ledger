namespace Ledger.Modules.Debts.Contracts;

public sealed record DebtParty(
    Guid PersonId,
    string DisplayName,
    Guid? UserId);

/// <summary>A loan, split or settlement was recorded in the ledger of every registered user involved.</summary>
public sealed record DebtOperationCompleted(
    Guid OperationId,
    string Kind,
    Guid InitiatorUserId,
    Guid? GroupId,
    IReadOnlyList<DebtParty> Parties);

/// <summary>
/// A person was merged into a registered user: debts, operations and group members now use <c>UserId</c>, and the
/// user's books hold a copy of the shared history. <c>Parties</c> are the people the user now shares debts with
/// (the user included), so they see each other in their contacts.
/// </summary>
public sealed record PersonMerged(
    Guid RequestId,
    Guid PreviousPersonId,
    Guid UserId,
    IReadOnlyList<DebtParty> Parties);

/// <summary>The merge could not be applied (e.g. the user's books are closed for those dates). Nothing changed.</summary>
public sealed record PersonMergeFailed(Guid RequestId, string Reason);
