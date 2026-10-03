using Ledger.BuildingBlocks.Domain;

namespace Ledger.Modules.Investments.Domain;

public enum TradeKind
{
    Buy,
    Sell,
    /// <summary>A holding the user already owned before using Ledger (posted against Opening balances).</summary>
    Opening
}

public enum TradeStatus
{
    Pending,
    Completed,
    Rejected
}

/// <summary>
/// A purchase, a sale or an already owned holding of an asset. Trades are append-only: a mistake is
/// fixed with an opposite trade. The ledger records it asynchronously (<see cref="TradeStatus"/>).
/// </summary>
public sealed class Trade
{
    public const int DescriptionMaxLength = 500;
    public const int ReasonMaxLength = 1000;

    private Trade()
    {
    }

    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public Guid AssetId { get; private set; }
    public TradeKind Kind { get; private set; }
    public decimal Quantity { get; private set; }

    /// <summary>Price per unit in the asset currency.</summary>
    public decimal UnitPrice { get; private set; }

    /// <summary>Quantity × unit price rounded to the cent, in the asset currency: what the ledger records.</summary>
    public decimal Total { get; private set; }

    public Currency Currency { get; private set; }

    public Guid? WalletId { get; private set; }

    /// <summary>Currency of the wallet, as reported by the ledger once the trade is recorded.</summary>
    public Currency? WalletCurrency { get; private set; }

    /// <summary>Money paid or received in the wallet's currency, when it differs from the asset currency.</summary>
    public decimal? WalletAmount { get; private set; }

    /// <summary>Sales: cost basis of the units sold (average cost).</summary>
    public decimal CostRemoved { get; private set; }

    /// <summary>Sales: <see cref="Total"/> − <see cref="CostRemoved"/>.</summary>
    public decimal RealizedGain => Kind == TradeKind.Sell ? Total - CostRemoved : 0;

    /// <summary>Optional price of the other side, recorded with the trade (the sell price on a purchase, the buy price on a sale).</summary>
    public decimal? CounterPrice { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }
    public string? Description { get; private set; }
    public TradeStatus Status { get; private set; }
    public string? RejectionReason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Price side the trade itself tells: what was paid for a purchase, what was received for a sale.</summary>
    public PriceSide Side => Kind == TradeKind.Sell ? PriceSide.Sell : PriceSide.Buy;

    internal static Trade Create(
        Asset asset,
        TradeKind kind,
        decimal quantity,
        decimal unitPrice,
        decimal total,
        decimal costRemoved,
        DateTimeOffset occurredAt,
        string? description,
        Guid? walletId,
        decimal? walletAmount,
        decimal? counterPrice)
    {
        description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();

        if (description?.Length > DescriptionMaxLength)
            throw new DomainException(
                "investment.description_too_long",
                $"The description cannot be longer than {DescriptionMaxLength} characters.");

        if (kind == TradeKind.Opening)
        {
            if (walletId is not null)
                throw new DomainException(
                    "investment.opening_with_wallet", "An already owned holding doesn't use a wallet.");
        }
        else if (walletId is null)
        {
            throw new DomainException("investment.wallet_required", "Choose the wallet that pays or receives the money.");
        }

        if (walletAmount is { } amount)
            Money.EnsurePositive(amount, "wallet amount");

        return new Trade
        {
            Id = Guid.NewGuid(),
            OwnerId = asset.OwnerId,
            AssetId = asset.Id,
            Kind = kind,
            Quantity = quantity,
            UnitPrice = unitPrice,
            Total = total,
            Currency = asset.Currency,
            WalletId = walletId,
            WalletAmount = walletAmount,
            CostRemoved = costRemoved,
            CounterPrice = counterPrice,
            OccurredAt = occurredAt,
            Description = description,
            Status = TradeStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    /// <returns>False when it was already answered (duplicate answer).</returns>
    public bool Complete(Currency? walletCurrency)
    {
        if (Status != TradeStatus.Pending)
            return false;

        Status = TradeStatus.Completed;
        WalletCurrency = walletCurrency;

        // Same currency: the wallet moved exactly the total, an amount sent anyway was not used.
        if (walletCurrency == Currency)
            WalletAmount = null;

        return true;
    }

    /// <returns>False when it was already answered (duplicate answer).</returns>
    public bool Reject(string reason)
    {
        if (Status != TradeStatus.Pending)
            return false;

        Status = TradeStatus.Rejected;
        RejectionReason = reason.Length > ReasonMaxLength ? reason[..ReasonMaxLength] : reason;
        return true;
    }

    /// <summary>The prices the trade records once it is in the books.</summary>
    public IEnumerable<AssetPrice> Prices()
    {
        yield return AssetPrice.FromTrade(this, Side, UnitPrice);

        if (CounterPrice is { } counter)
            yield return AssetPrice.FromTrade(this, Side == PriceSide.Buy ? PriceSide.Sell : PriceSide.Buy, counter);
    }
}
