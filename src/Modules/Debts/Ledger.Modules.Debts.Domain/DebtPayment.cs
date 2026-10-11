using Ledger.BuildingBlocks.Domain;

namespace Ledger.Modules.Debts.Domain;

/// <summary>
/// How much of one debt a settlement (a payment between two people) paid. A debt can be paid by several.
/// Rows are never deleted: they count while their settlement is the current version (Completed),
/// are a reservation while it is Pending, and are history once it is replaced, deleted or refused.
/// </summary>
public sealed class DebtPayment
{
    private DebtPayment()
    {
    }

    public Guid Id { get; private set; }
    public Guid SettlementOperationId { get; private set; }
    public Guid DebtId { get; private set; }
    public decimal Amount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static DebtPayment Create(Guid settlementOperationId, Guid debtId, decimal amount)
    {
        Money.EnsurePositive(amount);

        return new DebtPayment
        {
            Id = Guid.NewGuid(),
            SettlementOperationId = settlementOperationId,
            DebtId = debtId,
            Amount = amount,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }
}
