using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static Ledger.IntegrationTests.ApiHelpers;

namespace Ledger.IntegrationTests;

/// <summary>Investments: assets, buys, sales, opening holdings, prices and their valuation.</summary>
[Collection(LedgerCollection.Name)]
public class InvestmentsTests(LedgerApiFactory factory)
{
    private static string NewUser(string name) => $"{name}-{Guid.NewGuid():N}"[..20];

    [Fact]
    public async Task Buying_and_selling_moves_money_at_cost_and_values_holdings_at_the_sell_price()
    {
        var alice = factory.ClientFor(NewUser("alice"));
        await alice.GetJsonAsync("/api/me");
        var dollars = await alice.CreateReadyWalletAsync("Dollars", "USD", 1000);
        var btc = await CreateAssetAsync(alice, "Bitcoin", "BTC", "Crypto", "USD");

        var buy = await CompletedTradeAsync(alice, await alice.PostJsonAsync("/api/investments/trades/buy",
            new { assetId = btc, quantity = 2, unitPrice = 100, walletId = dollars, sellPrice = 95 }, HttpStatusCode.Accepted));
        Assert.Equal("Completed", buy.GetProperty("status").GetString());
        Assert.Equal("USD", buy.GetProperty("walletCurrency").GetString());

        await EventuallyAsync(async () => await alice.WalletBalanceAsync(dollars) == 800 ? (object)true : null, "the purchase to be paid");
        var asset = await alice.GetJsonAsync($"/api/investments/assets/{btc}");
        Assert.Equal(2, asset.GetProperty("quantity").GetDecimal());
        Assert.Equal(200, asset.GetProperty("costBasis").GetDecimal());
        Assert.Equal("Sell", asset.GetProperty("valuationSource").GetString());
        Assert.Equal(190, asset.GetProperty("marketValue").GetDecimal());
        Assert.Equal(-10, asset.GetProperty("unrealizedGain").GetDecimal());

        // The ledger's net worth is money only: investments are valued by the Investments module.
        Assert.Equal(800, (await alice.TotalsAsync("USD")).GetProperty("net").GetDecimal());

        await alice.PostJsonAsync($"/api/investments/assets/{btc}/prices", new { sellPrice = 150 }, HttpStatusCode.NoContent);
        var summary = (await alice.GetJsonAsync("/api/investments/summary")).EnumerateArray().Single();
        Assert.Equal(300, summary.GetProperty("value").GetDecimal());
        Assert.Equal(100, summary.GetProperty("unrealizedGain").GetDecimal());

        var sale = await CompletedTradeAsync(alice, await alice.PostJsonAsync("/api/investments/trades/sell",
            new { assetId = btc, quantity = 1, unitPrice = 150, walletId = dollars }, HttpStatusCode.Accepted));
        Assert.Equal("Completed", sale.GetProperty("status").GetString());
        Assert.Equal(100, sale.GetProperty("costRemoved").GetDecimal());
        Assert.Equal(50, sale.GetProperty("realizedGain").GetDecimal());

        await EventuallyAsync(async () => await alice.WalletBalanceAsync(dollars) == 950 ? (object)true : null, "the sale to be received");
        asset = await alice.GetJsonAsync($"/api/investments/assets/{btc}");
        Assert.Equal(1, asset.GetProperty("quantity").GetDecimal());
        Assert.Equal(100, asset.GetProperty("costBasis").GetDecimal());
        Assert.Equal(50, asset.GetProperty("realizedGain").GetDecimal());

        // A realized gain is neither income nor an expense.
        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-1).ToString("O"));
        var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddMinutes(1).ToString("O"));
        var report = await alice.GetJsonAsync($"/api/reports?from={from}&to={to}&currency=USD&timeZone=UTC");
        Assert.Equal(0, report.GetProperty("totalIncome").GetDecimal());
        Assert.Equal(0, report.GetProperty("totalExpenses").GetDecimal());
        Assert.Equal(950, report.GetProperty("buckets").EnumerateArray().Last().GetProperty("netWorth").GetDecimal());

        var investments = await alice.GetJsonAsync($"/api/investments/report?from={from}&to={to}&currency=USD&timeZone=UTC");
        Assert.Equal(report.GetProperty("buckets").GetArrayLength(), investments.GetProperty("buckets").GetArrayLength());
        Assert.Equal(150, investments.GetProperty("buckets").EnumerateArray().Last().GetProperty("value").GetDecimal());
        Assert.Equal(50, investments.GetProperty("realizedGain").GetDecimal());

        // Investment transactions are listed in the ledger but cannot be edited there.
        var kinds = (await alice.GetJsonAsync($"/api/transactions?walletId={dollars}&pageSize=50")).GetProperty("items")
            .EnumerateArray().Where(t => t.GetProperty("kind").GetString()!.StartsWith("Investment")).ToList();
        Assert.Equal(2, kinds.Count);
        Assert.All(kinds, t => Assert.False(t.GetProperty("editable").GetBoolean()));

        // Not enough money: the ledger refuses the purchase and the holding does not change.
        var refused = await CompletedTradeAsync(alice, await alice.PostJsonAsync("/api/investments/trades/buy",
            new { assetId = btc, quantity = 100, unitPrice = 100, walletId = dollars }, HttpStatusCode.Accepted));
        Assert.Equal("Rejected", refused.GetProperty("status").GetString());
        Assert.Contains("enough money", refused.GetProperty("rejectionReason").GetString());
        Assert.Equal(1, (await alice.GetJsonAsync($"/api/investments/assets/{btc}")).GetProperty("quantity").GetDecimal());

        await AssertRefusedAsync(alice, "/api/investments/trades/sell",
            new { assetId = btc, quantity = 5, unitPrice = 150, walletId = dollars }, "investment.insufficient_quantity");
        await AssertRefusedAsync(alice, "/api/investments/trades/buy",
            new { assetId = btc, quantity = 1, unitPrice = 150, walletId = dollars, occurredAt = DateTimeOffset.UtcNow.AddDays(-3) },
            "investment.backdated");

        var trades = await alice.GetJsonAsync($"/api/investments/trades?assetId={btc}");
        Assert.Equal(3, trades.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Wallets_in_another_currency_go_through_currency_exchange_and_owned_holdings_use_opening_balances()
    {
        var bob = factory.ClientFor(NewUser("bob"));
        await bob.GetJsonAsync("/api/me");
        var pesos = await bob.CreateReadyWalletAsync("Pesos", "ARS", 1_000_000);
        var apple = await CreateAssetAsync(bob, "Apple", "AAPL", "Stock", "USD");

        var missingAmount = await CompletedTradeAsync(bob, await bob.PostJsonAsync("/api/investments/trades/buy",
            new { assetId = apple, quantity = 1, unitPrice = 200, walletId = pesos }, HttpStatusCode.Accepted));
        Assert.Equal("Rejected", missingAmount.GetProperty("status").GetString());

        var buy = await CompletedTradeAsync(bob, await bob.PostJsonAsync("/api/investments/trades/buy",
            new { assetId = apple, quantity = 1.5m, unitPrice = 200, walletId = pesos, walletAmount = 360_000 }, HttpStatusCode.Accepted));
        Assert.Equal("Completed", buy.GetProperty("status").GetString());
        Assert.Equal("ARS", buy.GetProperty("walletCurrency").GetString());
        await EventuallyAsync(async () => await bob.WalletBalanceAsync(pesos) == 640_000 ? (object)true : null, "the pesos to be paid");

        var transactions = (await bob.GetJsonAsync($"/api/transactions?walletId={pesos}&pageSize=50")).GetProperty("items");
        var purchase = transactions.EnumerateArray().Single(t => t.GetProperty("kind").GetString() == "InvestmentBuy");
        var purposes = purchase.GetProperty("postings").EnumerateArray()
            .Select(p => p.GetProperty("accountPurpose").GetString()).ToList();
        Assert.Contains("CurrencyExchange", purposes);
        Assert.Contains("Investment", purposes);

        // An ETF already owned before using Ledger: no wallet moves.
        var etf = await CreateAssetAsync(bob, "World ETF", null, "Etf", "EUR");
        var opening = await CompletedTradeAsync(bob, await bob.PostJsonAsync("/api/investments/trades/opening",
            new { assetId = etf, quantity = 3, unitPrice = 10.5m, occurredAt = DateTimeOffset.UtcNow.AddDays(-30) }, HttpStatusCode.Accepted));
        Assert.Equal("Completed", opening.GetProperty("status").GetString());

        var summary = (await bob.GetJsonAsync("/api/investments/summary")).EnumerateArray()
            .ToDictionary(t => t.GetProperty("currency").GetString()!);
        Assert.Equal(31.5m, summary["EUR"].GetProperty("value").GetDecimal());
        Assert.Equal(300, summary["USD"].GetProperty("value").GetDecimal());

        // A locked period refuses trades dated in it.
        var lockResponse = await bob.PutAsJsonAsync("/api/settings/books-lock", new { lockedUntil = DateTimeOffset.UtcNow.AddDays(-5) }, Json);
        Assert.True(lockResponse.IsSuccessStatusCode);
        var gold = await CreateAssetAsync(bob, "Gold", null, "Other", "USD");
        var locked = await CompletedTradeAsync(bob, await bob.PostJsonAsync("/api/investments/trades/opening",
            new { assetId = gold, quantity = 1, unitPrice = 2000, occurredAt = DateTimeOffset.UtcNow.AddDays(-10) }, HttpStatusCode.Accepted));
        Assert.Equal("Rejected", locked.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Only_assets_no_longer_held_can_be_archived()
    {
        var carol = factory.ClientFor(NewUser("carol"));
        await carol.GetJsonAsync("/api/me");
        var asset = await CreateAssetAsync(carol, "Fund", null, "Fund", "EUR");

        await CompletedTradeAsync(carol, await carol.PostJsonAsync("/api/investments/trades/opening",
            new { assetId = asset, quantity = 1, unitPrice = 10 }, HttpStatusCode.Accepted));

        await AssertRefusedAsync(carol, $"/api/investments/assets/{asset}/archive", new { }, "investment.not_empty");

        Assert.Contains(
            (await carol.GetJsonAsync("/api/investments/assets")).EnumerateArray(),
            a => a.GetProperty("id").GetGuid() == asset);
    }

    private static async Task<Guid> CreateAssetAsync(HttpClient client, string name, string? symbol, string type, string currency)
        => (await client.PostJsonAsync("/api/investments/assets", new { name, symbol, type, currency })).GetProperty("id").GetGuid();

    private static async Task<JsonElement> CompletedTradeAsync(HttpClient client, JsonElement trade)
    {
        var id = trade.GetProperty("id").GetGuid();

        return await EventuallyAsync<JsonElement?>(async () =>
        {
            var current = await client.GetJsonAsync($"/api/investments/trades/{id}");
            return current.GetProperty("status").GetString() == "Pending" ? null : current;
        }, $"trade {id}") ?? default;
    }

    private static async Task AssertRefusedAsync(HttpClient client, string path, object body, string code)
    {
        var response = await client.PostAsJsonAsync(path, body, Json);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(code, await response.Content.ReadAsStringAsync());
    }
}
