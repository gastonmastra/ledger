using Ledger.BuildingBlocks.Domain;

namespace Ledger.Modules.Debts.Domain;

/// <summary>
/// Money one person owes another, produced by a loan, an existing debt, a split or a payment that paid more than
/// what was owed. Outside groups it is paid by <see cref="DebtPayment"/>s (maybe partially, oldest first);
/// group debts are settled all at once when the group is even (<see cref="SettlementOperationId"/>).
/// </summary>
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

    /// <summary>Paid so far by completed payments (outside groups) or the whole amount once settled.</summary>
    public decimal Paid { get; private set; }

    public decimal Remaining => Amount - Paid;
    public bool IsPartiallyPaid => Status == DebtStatus.Open && Paid > 0;

    public Currency Currency { get; private set; }
    public DebtStatus Status { get; private set; }

    /// <summary>Group debts only: the group settlement that settled it, while it is in progress or once done.</summary>
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
        Paid = Amount;
        Status = DebtStatus.Settled;
        SettledAt = DateTimeOffset.UtcNow;
    }

    /// <summary>A payment was recorded (outside groups): fully paid debts become Settled.</summary>
    public void ApplyPayment(decimal amount)
    {
        Money.EnsurePositive(amount);

        if (Status != DebtStatus.Open)
            throw new DomainException("debt.not_open", "Only open debts can be paid.");

        if (amount > Remaining)
            throw new DomainException("payment.too_much", "A payment cannot pay more than what is left of a debt.");

        Paid += amount;

        if (Remaining == 0)
        {
            Status = DebtStatus.Settled;
            SettledAt = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>A payment was changed or deleted: what it paid is owed again.</summary>
    public void RemovePayment(decimal amount)
    {
        Money.EnsurePositive(amount);

        if (Status is not (DebtStatus.Open or DebtStatus.Settled))
            return;

        Paid = Math.Max(0, Paid - amount);
        Status = DebtStatus.Open;
        SettledAt = null;
    }

    public void CancelSettlement(Guid settlementOperationId)
    {
        if (Status == DebtStatus.Open && SettlementOperationId == settlementOperationId)
            SettlementOperationId = null;
    }

    /// <summary>The settlement that paid the debt was undone: the debt is open again.</summary>
    public void Reopen(Guid settlementOperationId)
    {
        if (SettlementOperationId != settlementOperationId)
            return;

        Status = DebtStatus.Open;
        Paid = 0;
        SettlementOperationId = null;
        SettledAt = null;
    }

    /// <summary>A settlement moved to its new version (e.g. a different date).</summary>
    public void MoveSettlement(Guid from, Guid to)
    {
        if (SettlementOperationId == from)
            SettlementOperationId = to;
    }

    public void Supersede() => Status = DebtStatus.Superseded;

    public bool IsPaid => Status == DebtStatus.Settled || IsSettling;

    /// <summary>Creates the debt a payment leaves when it pays more than what was owed.</summary>
    public static Debt ForExtraPayment(Guid settlementOperationId, Guid payerPersonId, Guid payeePersonId, decimal extra, Currency currency)
        => Create(settlementOperationId, null, payerPersonId, payeePersonId, extra, currency);
}
