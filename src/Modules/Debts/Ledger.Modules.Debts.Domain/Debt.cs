using Ledger.BuildingBlocks.Domain;

namespace Ledger.Modules.Debts.Domain;

/// <summary>Money one person owes another, produced by a loan or a split.</summary>
public sealed class Debt
{
    private Debt()
    {
    }

    public Guid Id { get; private set; }

    /// <summary>The loan or split that created the debt.</summary>
    public Guid OperationId { get; private set; }

    public Guid? GroupId { get; private set; }
    public Guid CreditorPersonId { get; private set; }
    public Guid DebtorPersonId { get; private set; }
    public decimal Amount { get; private set; }
    public Currency Currency { get; private set; }
    public DebtStatus Status { get; private set; }

    /// <summary>The settlement paying the debt, while it is in progress or once done.</summary>
    public Guid? SettlementOperationId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? SettledAt { get; private set; }

    public bool IsSettling => Status == DebtStatus.Open && SettlementOperationId is not null;

    public static Debt Create(
        Guid operationId,
        Guid? groupId,
        Guid creditorPersonId,
        Guid debtorPersonId,
        decimal amount,
        Currency currency)
    {
        Money.EnsurePositive(amount);

        if (creditorPersonId == debtorPersonId)
            throw new DomainException("debt.self", "A person cannot owe money to themselves.");

        return new Debt
        {
            Id = Guid.NewGuid(),
            OperationId = operationId,
            GroupId = groupId,
            CreditorPersonId = creditorPersonId,
            DebtorPersonId = debtorPersonId,
            Amount = amount,
            Currency = currency,
            Status = DebtStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public bool Involves(Guid personId) => CreditorPersonId == personId || DebtorPersonId == personId;

    public void Activate()
    {
        if (Status == DebtStatus.Pending)
            Status = DebtStatus.Open;
    }

    public void Reject()
    {
        if (Status == DebtStatus.Pending)
            Status = DebtStatus.Rejected;
    }

    public void StartSettlement(Guid settlementOperationId)
    {
        if (Status != DebtStatus.Open)
            throw new DomainException("debt.not_open", "Only open debts can be settled.");

        if (IsSettling)
            throw new DomainException("debt.settling", "The debt is already being settled.");

        SettlementOperationId = settlementOperationId;
    }

    public void CompleteSettlement(Guid settlementOperationId)
    {
        if (Status == DebtStatus.Settled)
            return;

        SettlementOperationId = settlementOperationId;
        Status = DebtStatus.Settled;
        SettledAt = DateTimeOffset.UtcNow;
    }

    public void CancelSettlement(Guid settlementOperationId)
    {
        if (Status == DebtStatus.Open && SettlementOperationId == settlementOperationId)
            SettlementOperationId = null;
    }
}
