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
    /// <summary>One debt outside groups.</summary>
    Debt,
    /// <summary>Every open debt (outside groups) with a contact, in one currency.</summary>
    Contact,
    /// <summary>One suggested payment of a group's simplified balances.</summary>
    GroupPair
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
