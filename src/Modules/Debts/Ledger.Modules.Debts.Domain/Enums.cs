namespace Ledger.Modules.Debts.Domain;

public enum OperationKind
{
    Loan,
    Split,
    Settlement,
    /// <summary>A debt that already existed before using Ledger.</summary>
    InitialDebt
}

public enum InitialDebtDirection
{
    /// <summary>I already owed the contact.</summary>
    IOwe,
    /// <summary>The contact already owed me.</summary>
    TheyOweMe
}

public enum OperationStatus
{
    /// <summary>Waiting for the ledger to record it.</summary>
    Pending,
    Completed,
    /// <summary>The ledger refused it (e.g. not enough money in the wallet).</summary>
    Rejected,
    /// <summary>Replaced by a newer version (an edit).</summary>
    Superseded,
    /// <summary>Deleted by a user (it can be restored).</summary>
    Deleted
}

public enum SplitMethod
{
    Equal,
    Custom,
    Percent
}

public enum SettlementScope
{
    /// <summary>Legacy (before partial payments): one debt outside groups, fully. Kept for stored settlements.</summary>
    Debt,
    /// <summary>Legacy (before partial payments): every open debt with a contact, fully. Kept for stored settlements.</summary>
    Contact,
    /// <summary>One suggested payment of a group's simplified balances.</summary>
    GroupPair,
    /// <summary>An amount paid to a person outside groups, applied to the debts with them oldest first.</summary>
    Payment
}

public enum PaymentDirection
{
    /// <summary>The contact paid me.</summary>
    TheyPaidMe,
    /// <summary>I paid the contact.</summary>
    IPaidThem
}

public enum DebtStatus
{
    Pending,
    Open,
    Settled,
    Rejected,
    /// <summary>Its operation was changed or deleted.</summary>
    Superseded
}

public enum ChangeKind
{
    Revise,
    Delete,
    Restore
}

public enum ChangeStatus
{
    /// <summary>Waiting for the linked users to accept or reject it.</summary>
    Pending,
    /// <summary>Everybody answered; the ledger is recording it.</summary>
    Applying,
    /// <summary>Recorded (maybe disputed by some users).</summary>
    Applied,
    /// <summary>The ledger refused it (e.g. not enough money, a locked period).</summary>
    Failed
}

public enum ApprovalStatus
{
    Pending,
    Accepted,
    Rejected
}
