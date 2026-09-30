using Ledger.BuildingBlocks.Domain;
using Ledger.Modules.Wallets.Domain;

namespace Ledger.UnitTests;

public class WalletTests
{
    [Fact]
    public void Only_empty_wallets_can_be_archived()
    {
        var wallet = Wallet.Create(Guid.NewGuid(), "Bank", Currency.EUR, 100);

        Assert.Equal("wallet.not_empty", Assert.Throws<DomainException>(wallet.Archive).Code);

        wallet.ApplyLedgerBalance(0, 2, version: 3);
        wallet.Archive();

        Assert.Equal(WalletStatus.Archived, wallet.Status);
    }

    [Fact]
    public void Wallets_with_transactions_cannot_be_deleted()
    {
        Wallet.Create(Guid.NewGuid(), "Empty", Currency.EUR, 0).EnsureCanBeDeleted();

        var used = Wallet.Create(Guid.NewGuid(), "Used", Currency.EUR, 10);
        Assert.Throws<DomainException>(used.EnsureCanBeDeleted);
    }

    [Fact]
    public void Stale_balance_reports_are_ignored()
    {
        var wallet = Wallet.Create(Guid.NewGuid(), "Bank", Currency.EUR, 0);

        wallet.ApplyLedgerBalance(80, 2, version: 3);
        wallet.ApplyLedgerBalance(100, 1, version: 2);

        Assert.Equal(80, wallet.Balance);
        Assert.True(wallet.IsReady);
    }

    [Fact]
    public void Opening_balance_cannot_be_negative()
        => Assert.Throws<DomainException>(() => Wallet.Create(Guid.NewGuid(), "Bank", Currency.EUR, -1));
}
