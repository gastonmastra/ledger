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
