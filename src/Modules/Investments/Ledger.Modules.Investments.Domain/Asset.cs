using Ledger.BuildingBlocks.Domain;

namespace Ledger.Modules.Investments.Domain;

public enum AssetType
{
    Stock,
    Etf,
    Crypto,
    Bond,
    Fund,
    Other
}

public enum AssetStatus
{
    Active,
    Archived
}

/// <summary>
/// Something the user invests in (a share, an ETF, a crypto…), quoted in one currency, together with
/// the user's holding of it: the quantity owned, its cost basis (average cost) and the realized gains.
/// The holding changes only when the ledger has recorded a trade.
/// </summary>
public sealed class Asset
{
    public const int NameMaxLength = 100;
    public const int SymbolMaxLength = 20;

    private Asset()
    {
        Name = null!;
    }

    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public string Name { get; private set; }
    public string? Symbol { get; private set; }
    public AssetType Type { get; private set; }

    /// <summary>Quote currency: prices and the cost basis are in this currency.</summary>
    public Currency Currency { get; private set; }

    public AssetStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Units owned (recorded trades only).</summary>
    public decimal Quantity { get; private set; }

    /// <summary>What the units owned cost (average cost method).</summary>
    public decimal CostBasis { get; private set; }

    /// <summary>Sum of the gains (positive) and losses (negative) of every sale.</summary>
    public decimal RealizedGain { get; private set; }

    /// <summary>Date of the latest recorded trade. Trades can't be dated before it.</summary>
    public DateTimeOffset? LastTradeAt { get; private set; }

    /// <summary>The trade waiting for the ledger, if any. Only one at a time per asset.</summary>
    public Guid? PendingTradeId { get; private set; }

    /// <summary>Cost per unit owned, or null when nothing is owned.</summary>
    public decimal? AverageCost => Quantity == 0 ? null : Math.Round(CostBasis / Quantity, Prices.Decimals);

    public static Asset Create(Guid ownerId, string name, string? symbol, AssetType type, Currency currency)
        => new()
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            Name = ValidateName(name),
            Symbol = ValidateSymbol(symbol),
            Type = type,
            Currency = currency,
            Status = AssetStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow
        };

    public void Rename(string name, string? symbol)
    {
        EnsureActive();
        Name = ValidateName(name);
        Symbol = ValidateSymbol(symbol);
    }

    public void Archive()
    {
        EnsureActive();

        if (Quantity != 0 || PendingTradeId is not null)
            throw new DomainException(
                "investment.not_empty",
                "Only assets you no longer hold (and without a trade in progress) can be archived.");

        Status = AssetStatus.Archived;
    }

    /// <summary>
    /// Checks that a new trade can be recorded and reserves the asset for it until the ledger answers.
    /// For a sale, it also computes the cost of the units sold.
    /// </summary>
    public Trade StartTrade(
        TradeKind kind,
        decimal quantity,
        decimal unitPrice,
        DateTimeOffset occurredAt,
        string? description,
        Guid? walletId,
        decimal? walletAmount,
        decimal? counterPrice)
    {
        EnsureActive();

        if (PendingTradeId is not null)
            throw new DomainException(
                "investment.trade_pending",
                $"Another trade of '{Name}' is still being recorded. Try again in a moment.");

        if (LastTradeAt is { } last && occurredAt < last)
            throw new DomainException(
                "investment.backdated",
                FormattableString.Invariant(
                    $"Trades of '{Name}' can't be dated before its latest trade ({last:yyyy-MM-dd HH:mm} UTC)."));

        Quantities.EnsureValid(quantity);
        Prices.EnsureValid(unitPrice);

        if (counterPrice is { } other)
            Prices.EnsureValid(other);

        if (kind == TradeKind.Sell && quantity > Quantity)
            throw new DomainException(
                "investment.insufficient_quantity",
                FormattableString.Invariant($"You only hold {Quantity:0.############} of '{Name}'."));

        var total = Money.Round(quantity * unitPrice);

        if (total <= 0)
            throw new DomainException(
                "investment.total_zero",
                "Quantity × price is less than one cent: it can't be recorded.");

        var costRemoved = kind == TradeKind.Sell ? CostOf(quantity) : 0;

        var trade = Trade.Create(
            this, kind, quantity, unitPrice, total, costRemoved, occurredAt, description,
            walletId, walletAmount, counterPrice);

        PendingTradeId = trade.Id;

        return trade;
    }

    /// <summary>The ledger recorded the trade: the holding changes.</summary>
    public void Apply(Trade trade)
    {
        if (PendingTradeId != trade.Id)
            return;

        if (trade.Kind == TradeKind.Sell)
        {
            Quantity -= trade.Quantity;
            CostBasis -= trade.CostRemoved;
            RealizedGain += trade.RealizedGain;
        }
        else
        {
            Quantity += trade.Quantity;
            CostBasis += trade.Total;
        }

        LastTradeAt = trade.OccurredAt;
        PendingTradeId = null;
    }

    /// <summary>The ledger refused the trade: nothing changes, the asset is free for another trade.</summary>
    public void Release(Trade trade)
    {
        if (PendingTradeId == trade.Id)
            PendingTradeId = null;
    }

    /// <summary>Average cost of <paramref name="quantity"/> units, to the cent. Selling everything removes the whole cost basis.</summary>
    public decimal CostOf(decimal quantity)
        => quantity == Quantity ? CostBasis : Money.Round(CostBasis * quantity / Quantity);

    private void EnsureActive()
    {
        if (Status == AssetStatus.Archived)
            throw new DomainException("investment.archived", $"'{Name}' is archived.");
    }

    private static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("investment.name_required", "The asset name is required.");

        name = name.Trim();

        if (name.Length > NameMaxLength)
            throw new DomainException(
                "investment.name_too_long", $"The asset name cannot be longer than {NameMaxLength} characters.");

        return name;
    }

    private static string? ValidateSymbol(string? symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            return null;

        symbol = symbol.Trim().ToUpperInvariant();

        if (symbol.Length > SymbolMaxLength)
            throw new DomainException(
                "investment.symbol_too_long", $"The symbol cannot be longer than {SymbolMaxLength} characters.");

        return symbol;
    }
}
