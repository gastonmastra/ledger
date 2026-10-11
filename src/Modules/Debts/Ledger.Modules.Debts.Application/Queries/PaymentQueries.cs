using Ledger.BuildingBlocks.Application;
using Ledger.BuildingBlocks.Application.Exceptions;
using Ledger.BuildingBlocks.Domain;
using Ledger.Modules.Debts.Application.Operations;
using Ledger.Modules.Debts.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Modules.Debts.Application.Queries;

/// <summary>A payment that paid (part of) a debt.</summary>
/// <param name="EntryId">The payment as a logical operation (its versions share it).</param>
public sealed record DebtPaymentDto(
    Guid SettlementOperationId,
    Guid EntryId,
    OperationStatus Status,
    DateTimeOffset OccurredAt,
    string? Description,
    decimal Amount);

/// <summary>The payments (current ones; Pending while they are being recorded) that paid a debt, oldest first.</summary>
public sealed record GetDebtPaymentsQuery(Guid UserId, Guid DebtId) : IQuery<IReadOnlyList<DebtPaymentDto>>;

/// <summary>How a debt would be paid, from the viewer's point of view.</summary>
/// <param name="RemainingBefore">What is left of it now. RemainingAfter: once the payment is recorded.</param>
public sealed record PaymentPreviewLineDto(
    Guid DebtId,
    Guid OperationId,
    string? Description,
    DateTimeOffset OccurredAt,
    DebtDirection Direction,
    decimal DebtAmount,
    decimal RemainingBefore,
    decimal Paid,
    decimal RemainingAfter);

/// <param name="Lines">The debts it pays (also the ones that cancel out), oldest first.</param>
/// <param name="Extra">Paid beyond what was owed. ExtraDirection: whether that becomes owed to me or by me.</param>
/// <param name="BalanceAfter">What is left with the person once it is recorded (positive: they owe me).</param>
public sealed record PaymentPreviewDto(
    IReadOnlyList<PaymentPreviewLineDto> Lines,
    decimal Extra,
    DebtDirection? ExtraDirection,
    decimal BalanceBefore,
    decimal BalanceAfter);

/// <summary>
/// What recording a payment would do, without recording it. A new payment needs ContactId, Currency and Direction;
/// to preview an edit of a payment send its OperationId (and the new Amount) instead.
/// </summary>
public sealed record PreviewPaymentQuery(
    Guid UserId,
    decimal Amount,
    Guid? ContactId = null,
    string? Currency = null,
    PaymentDirection? Direction = null,
    Guid? OperationId = null) : IQuery<PaymentPreviewDto>;

internal sealed class PaymentQueryHandlers(IDebtsDbContext dbContext, PeopleDirectory people, PaymentBook payments)
    : IQueryHandler<GetDebtPaymentsQuery, IReadOnlyList<DebtPaymentDto>>,
      IQueryHandler<PreviewPaymentQuery, PaymentPreviewDto>
{
    public async Task<IReadOnlyList<DebtPaymentDto>> Handle(GetDebtPaymentsQuery query, CancellationToken cancellationToken)
    {
        var debt = await dbContext.Debts.AsNoTracking().SingleOrDefaultAsync(d => d.Id == query.DebtId, cancellationToken);

        if (debt is null || !debt.Involves(query.UserId))
            throw new NotFoundException("Debt", query.DebtId);

        var rows = await dbContext.Payments.AsNoTracking()
            .Where(p => p.DebtId == debt.Id)
            .ToListAsync(cancellationToken);

        var settlementIds = rows.Select(r => r.SettlementOperationId).ToList();
        var settlements = await dbContext.Operations.AsNoTracking()
            .Where(o => settlementIds.Contains(o.Id)
                        && (o.Status == OperationStatus.Pending || o.Status == OperationStatus.Completed))
            .ToDictionaryAsync(o => o.Id, cancellationToken);

        return rows
            .Where(r => settlements.ContainsKey(r.SettlementOperationId))
            .Select(r =>
            {
                var s = settlements[r.SettlementOperationId];
                return new DebtPaymentDto(s.Id, s.EntryId, s.Status, s.OccurredAt, s.Description, r.Amount);
            })
            .OrderBy(p => p.OccurredAt)
            .ToList();
    }

    public async Task<PaymentPreviewDto> Handle(PreviewPaymentQuery query, CancellationToken cancellationToken)
    {
        if (query.Amount < 0)
            throw new DomainException("payment.amount_positive", "The amount cannot be negative.");

        var me = query.UserId;
        Guid payer, payee, other;
        Currency currency;
        Guid? replacing = null;

        if (query.OperationId is { } operationId)
        {
            var operation = await dbContext.Operations.AsNoTracking().SingleOrDefaultAsync(o => o.Id == operationId, cancellationToken);

            if (operation is null
                || operation.Kind != OperationKind.Settlement
                || operation.GroupId is not null
                || (operation.FromPersonId != me && operation.ToPersonId != me))
                throw new NotFoundException("Payment", operationId);

            payer = operation.FromPersonId!.Value;
            payee = operation.ToPersonId!.Value;
            other = payer == me ? payee : payer;
            currency = operation.Currency;
            replacing = operation.Id;
        }
        else
        {
            if (query.ContactId is null || query.Currency is null || query.Direction is null)
                throw new DomainException("payment.incomplete", "Choose the person, the currency and who paid.");

            var contact = await people.ContactAsync(me, query.ContactId.Value, cancellationToken);
            currency = Currencies.Parse(query.Currency);
            other = contact.PersonId;
            payer = query.Direction == PaymentDirection.IPaidThem ? me : other;
            payee = payer == me ? other : me;
        }

        var payable = await payments.PayableAsync(me, other, currency, replacing, cancellationToken);
        var allocation = PaymentAllocation.Allocate(payer, payee, query.Amount, payable.Open);
        var paid = allocation.Allocations.ToDictionary(a => a.DebtId, a => a.Amount);

        decimal Signed(OpenDebt d, decimal amount) => d.CreditorPersonId == me ? amount : -amount;

        var lines = payable.Open
            .Where(d => paid.ContainsKey(d.DebtId))
            .Select(d =>
            {
                var debt = payable.Debts[d.DebtId];
                var origin = payable.Origins[d.DebtId];

                return new PaymentPreviewLineDto(
                    d.DebtId,
                    debt.OperationId,
                    origin.Description,
                    origin.OccurredAt,
                    d.CreditorPersonId == me ? DebtDirection.OwedToMe : DebtDirection.IOwe,
                    debt.Amount,
                    d.Available,
                    paid[d.DebtId],
                    d.Available - paid[d.DebtId]);
            })
            .ToList();

        var before = payable.Open.Sum(d => Signed(d, d.Available));
        var extraSigned = allocation.Extra == 0 ? 0 : payer == me ? allocation.Extra : -allocation.Extra;
        var after = payable.Open.Sum(d => Signed(d, d.Available - paid.GetValueOrDefault(d.DebtId))) + extraSigned;

        return new PaymentPreviewDto(
            lines,
            allocation.Extra,
            allocation.Extra == 0 ? null : payer == me ? DebtDirection.OwedToMe : DebtDirection.IOwe,
            before,
            after);
    }
}
