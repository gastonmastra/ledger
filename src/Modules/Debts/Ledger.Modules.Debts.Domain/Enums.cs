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
    Rejected
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
    Rejected
}
