using Ledger.BuildingBlocks.Domain;
using Ledger.Modules.Transactions.Domain;

namespace Ledger.UnitTests;

public class LedgerAccountTests
{
    private static LedgerAccount Wallet(Guid owner) => LedgerAccount.Create(
        new AccountId(Guid.NewGuid()), owner, "Bank", LedgerAccountType.Asset,
        LedgerAccountPurpose.Wallet, Currency.EUR, Guid.NewGuid());

    [Fact]
    public void A_wallet_cannot_be_overdrawn()
    {
        var wallet = Wallet(Guid.NewGuid());
        wallet.ApplyToWallet(50, Currency.EUR);

        var error = Assert.Throws<DomainException>(() => wallet.ApplyToWallet(-50.01m, Currency.EUR));

        Assert.Equal("ledger.insufficient_funds", error.Code);
        Assert.Equal(50, wallet.Balance);
    }

    [Fact]
    public void Spending_the_whole_balance_is_allowed_and_versions_grow()
    {
        var wallet = Wallet(Guid.NewGuid());
        var initialVersion = wallet.Version;

        wallet.ApplyToWallet(50, Currency.EUR);
        wallet.ApplyToWallet(-50, Currency.EUR);

        Assert.Equal(0, wallet.Balance);
        Assert.Equal(2, wallet.TransactionCount);
        Assert.Equal(initialVersion + 2, wallet.Version);
    }

    [Fact]
    public void A_wallet_only_accepts_its_currency()
    {
        var wallet = Wallet(Guid.NewGuid());

        Assert.Equal("ledger.currency_mismatch",
            Assert.Throws<DomainException>(() => wallet.ApplyToWallet(10, Currency.USD)).Code);
    }

    [Fact]
    public void Accounts_are_only_usable_by_their_owner_while_active()
    {
        var owner = Guid.NewGuid();
        var wallet = Wallet(owner);

        wallet.EnsureUsableBy(owner);
        Assert.Throws<DomainException>(() => wallet.EnsureUsableBy(Guid.NewGuid()));

        wallet.Archive();
        Assert.Throws<DomainException>(() => wallet.EnsureUsableBy(owner));
    }
}
