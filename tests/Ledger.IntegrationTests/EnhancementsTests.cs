using System.Net;
using System.Text.Json;
using static Ledger.IntegrationTests.ApiHelpers;

namespace Ledger.IntegrationTests;

/// <summary>Dates, opening balances, balance adjustments, initial debts and reports.</summary>
[Collection(LedgerCollection.Name)]
public class EnhancementsTests(LedgerApiFactory factory)
{
    private static string NewUser(string name) => $"{name}-{Guid.NewGuid():N}"[..20];

    [Fact]
    public async Task Movements_keep_the_chosen_date_and_future_dates_are_rejected()
    {
        var alice = factory.ClientFor(NewUser("alice"));
        await alice.GetJsonAsync("/api/me");
        var bank = await alice.CreateReadyWalletAsync("Bank", "EUR", 100);
        var when = new DateTimeOffset(2026, 3, 15, 18, 30, 0, TimeSpan.Zero);

        var created = await alice.PostJsonAsync("/api/transactions/expense",
            new { walletId = bank, amount = 10, occurredAt = when, description = "Dated" });
        var transaction = await alice.GetJsonAsync($"/api/transactions/{created.GetProperty("id").GetGuid()}");
        Assert.Equal(when, transaction.GetProperty("occurredAt").GetDateTimeOffset());

        var future = await alice.PostJsonAsync("/api/transactions/income",
            new { walletId = bank, amount = 10, occurredAt = DateTimeOffset.UtcNow.AddDays(2) }, HttpStatusCode.BadRequest);
        Assert.Contains("future", future.ToString());
    }

    [Fact]
    public async Task The_opening_balance_can_be_replaced_without_editing_history()
    {
        var alice = factory.ClientFor(NewUser("alice"));
        await alice.GetJsonAsync("/api/me");
        var openingDate = new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);
        var wallet = await alice.PostJsonAsync("/api/wallets",
            new { name = "Bank", currency = "EUR", openingBalance = 100, openingBalanceAt = openingDate });
        var walletId = wallet.GetProperty("id").GetGuid();
        await WaitReadyAsync(alice, walletId, 100);

        var opening = await alice.GetJsonAsync($"/api/transactions/opening-balance/{walletId}");
        Assert.Equal(100, opening.GetProperty("amount").GetDecimal());
        Assert.Equal(openingDate, opening.GetProperty("occurredAt").GetDateTimeOffset());

        await alice.PostJsonAsync("/api/transactions/expense", new { walletId, amount = 30 });

        var newDate = new DateTimeOffset(2025, 12, 31, 9, 0, 0, TimeSpan.Zero);
        await Put(alice, "/api/transactions/opening-balance", new { walletId, amount = 250, occurredAt = newDate, reason = "Wrong amount" });

        await EventuallyAsync(async () => await alice.WalletBalanceAsync(walletId) == 220 ? (object)true : null, "new balance");
        opening = await alice.GetJsonAsync($"/api/transactions/opening-balance/{walletId}");
        Assert.Equal(250, opening.GetProperty("amount").GetDecimal());
        Assert.Equal(newDate, opening.GetProperty("occurredAt").GetDateTimeOffset());

        // Only the current version is listed; the history keeps the replaced one and its reversal.
        var kinds = (await alice.GetJsonAsync($"/api/transactions?walletId={walletId}&pageSize=50"))
            .GetProperty("items").EnumerateArray().Select(t => t.GetProperty("kind").GetString()).ToList();
        Assert.Single(kinds, k => k == "OpeningBalance");

        var withHistory = (await alice.GetJsonAsync($"/api/transactions?walletId={walletId}&pageSize=50&includeHistory=true"))
            .GetProperty("items").EnumerateArray().Select(t => t.GetProperty("kind").GetString()).ToList();
        Assert.Contains("Reversal", withHistory);
        Assert.Equal(2, withHistory.Count(k => k == "OpeningBalance"));

        // Lowering it below what was already spent would overdraw the wallet.
        var response = await Put(alice, "/api/transactions/opening-balance", new { walletId, amount = 10, reason = "Too low" }, expectSuccess: false);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(220, await alice.WalletBalanceAsync(walletId));
    }

    [Fact]
    public async Task A_balance_adjustment_sets_the_real_balance()
    {
        var alice = factory.ClientFor(NewUser("alice"));
        await alice.GetJsonAsync("/api/me");
        var bank = await alice.CreateReadyWalletAsync("Bank", "EUR", 100);

        await alice.PostJsonAsync("/api/transactions/balance-adjustment",
            new { walletId = bank, actualBalance = 87.5m, description = "Bank fees" });

        await EventuallyAsync(async () => await alice.WalletBalanceAsync(bank) == 87.5m ? (object)true : null, "adjusted");
        Assert.Equal(-12.5m, (await alice.TotalsAsync("EUR")).GetProperty("adjustments").GetDecimal());

        var same = await alice.PostJsonAsync("/api/transactions/balance-adjustment",
            new { walletId = bank, actualBalance = 87.5m }, HttpStatusCode.BadRequest);
        Assert.Equal("adjustment.no_difference", same.GetProperty("code").GetString());
    }

    [Fact]
    public async Task An_initial_debt_is_mirrored_and_can_be_settled()
    {
        var gastonName = NewUser("gaston");
        var sofiName = NewUser("sofi");
        var gaston = factory.ClientFor(gastonName);
        var sofi = factory.ClientFor(sofiName);
        await gaston.GetJsonAsync("/api/me");
        var gastonId = (await gaston.GetJsonAsync("/api/me")).GetProperty("personId").GetGuid();
        await sofi.GetJsonAsync("/api/me");
        var wallet = await gaston.CreateReadyWalletAsync("Bank", "EUR", 100);

        var contact = await gaston.PostJsonAsync("/api/contacts", new { name = "Sofi" });
        var contactId = contact.GetProperty("id").GetGuid();
        await EventuallyAsync(async () =>
            (await gaston.PostAsync($"/api/contacts/{contactId}/link", Json(new { email = $"{sofiName}@test.local" })))
                .IsSuccessStatusCode ? (object)true : null, "Sofi to be linkable");

        // The Debts module learns about the link asynchronously. Probe it with an invalid split (no
        // wallet): it fails with "unknown person" until the link arrives, then with "wallet required".
        var sofiId = (await sofi.GetJsonAsync("/api/me")).GetProperty("personId").GetGuid();
        await EventuallyAsync(async () =>
        {
            var probe = await gaston.PostAsync("/api/debts/splits", Json(new
            {
                total = 1,
                currency = "EUR",
                method = "Equal",
                payers = new[] { new { personId = gastonId, amount = 1 } },
                participants = new[] { new { personId = gastonId }, new { personId = sofiId } }
            }));
            return (await probe.Content.ReadAsStringAsync()).Contains("split.wallet_required") ? (object)true : null;
        }, "the link to reach the Debts module");

        var operation = await EventuallyAsync<JsonElement?>(async () =>
        {
            var response = await gaston.PostAsync("/api/debts/initial", Json(new
            {
                contactId,
                direction = "IOwe",
                amount = 50,
                currency = "EUR",
                occurredAt = new DateTimeOffset(2026, 2, 1, 12, 0, 0, TimeSpan.Zero)
            }));
            return response.StatusCode == HttpStatusCode.Accepted
                ? JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync())
                : null;
        }, "the initial debt to be accepted");
        Assert.Equal("Completed", (await gaston.CompletedOperationAsync(operation!.Value)).GetProperty("status").GetString());

        Assert.Equal(50, (await gaston.TotalsAsync("EUR")).GetProperty("iOwe").GetDecimal());
        Assert.Equal(100, await gaston.WalletBalanceAsync(wallet)); // no money moved
        Assert.Equal(50, (await sofi.TotalsAsync("EUR")).GetProperty("owedToMe").GetDecimal());

        var debt = (await gaston.GetJsonAsync("/api/debts")).GetProperty("debts").EnumerateArray().Single();
        var settle = await gaston.PostJsonAsync("/api/debts/settlements",
            new { scope = "Debt", debtId = debt.GetProperty("id").GetGuid(), walletId = wallet }, HttpStatusCode.Accepted);
        Assert.Equal("Completed", (await gaston.CompletedOperationAsync(settle)).GetProperty("status").GetString());

        Assert.Equal(0, (await gaston.TotalsAsync("EUR")).GetProperty("iOwe").GetDecimal());
        await EventuallyAsync(async () => await gaston.WalletBalanceAsync(wallet) == 50 ? (object)true : null, "paid back");
        Assert.Equal(0, (await sofi.TotalsAsync("EUR")).GetProperty("owedToMe").GetDecimal());
        Assert.Single((await sofi.GetJsonAsync("/api/pending-assignments")).EnumerateArray());
    }

    [Fact]
    public async Task Reports_group_income_expenses_and_net_worth_by_period()
    {
        var alice = factory.ClientFor(NewUser("alice"));
        await alice.GetJsonAsync("/api/me");
        var start = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var wallet = await alice.PostJsonAsync("/api/wallets",
            new { name = "Bank", currency = "EUR", openingBalance = 1000, openingBalanceAt = start.AddDays(-10) });
        var walletId = wallet.GetProperty("id").GetGuid();
        await WaitReadyAsync(alice, walletId, 1000);

        var food = await alice.PostJsonAsync("/api/categories", new { name = "Food", type = "Expense" });
        await alice.PostJsonAsync("/api/transactions/income", new { walletId, amount = 500, occurredAt = start.AddDays(2) });
        await alice.PostJsonAsync("/api/transactions/expense",
            new { walletId, amount = 40, categoryId = food.GetProperty("id").GetGuid(), occurredAt = start.AddDays(3) });
        await alice.PostJsonAsync("/api/transactions/expense", new { walletId, amount = 10, occurredAt = start.AddDays(3) });
        await alice.PostJsonAsync("/api/transactions/balance-adjustment",
            new { walletId, actualBalance = 1440, occurredAt = start.AddDays(4) }); // −10, excluded from expenses

        var month = await alice.GetJsonAsync(
            $"/api/reports?from={Uri.EscapeDataString(start.ToString("O"))}&to={Uri.EscapeDataString(start.AddMonths(1).AddTicks(-1).ToString("O"))}&currency=EUR&timeZone=UTC");

        Assert.Equal("Day", month.GetProperty("granularity").GetString());
        Assert.Equal(31, month.GetProperty("buckets").GetArrayLength());
        Assert.Equal(500, month.GetProperty("totalIncome").GetDecimal());
        Assert.Equal(50, month.GetProperty("totalExpenses").GetDecimal());

        var buckets = month.GetProperty("buckets").EnumerateArray().ToList();
        Assert.Equal(1000, buckets[0].GetProperty("netWorth").GetDecimal());   // opening balance before the period
        Assert.Equal(1440, buckets[^1].GetProperty("netWorth").GetDecimal());

        var spending = month.GetProperty("spendingByCategory").EnumerateArray().ToList();
        Assert.Equal(40, spending.Single(s => s.GetProperty("name").GetString() == "Food").GetProperty("amount").GetDecimal());
        Assert.Equal(1440, month.GetProperty("walletBalances")[0].GetProperty("balance").GetDecimal());

        var year = await alice.GetJsonAsync(
            $"/api/reports?from={Uri.EscapeDataString(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).ToString("O"))}&to={Uri.EscapeDataString(new DateTimeOffset(2026, 12, 31, 23, 59, 59, TimeSpan.Zero).ToString("O"))}&timeZone=UTC");
        Assert.Equal("Month", year.GetProperty("granularity").GetString());
        Assert.Equal(12, year.GetProperty("buckets").GetArrayLength());
        Assert.Equal("EUR", year.GetProperty("currency").GetString());
    }

    [Fact]
    public async Task Dates_with_any_offset_are_accepted_and_stored_as_the_same_instant()
    {
        var alice = factory.ClientFor(NewUser("alice"));
        await alice.GetJsonAsync("/api/me");
        var bank = await alice.CreateReadyWalletAsync("Bank", "EUR", 100);
        var madrid = new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.FromHours(2));

        var created = await alice.PostJsonAsync("/api/transactions/expense", new { walletId = bank, amount = 5, occurredAt = madrid });
        var transaction = await alice.GetJsonAsync($"/api/transactions/{created.GetProperty("id").GetGuid()}");
        Assert.Equal(madrid.UtcDateTime, transaction.GetProperty("occurredAt").GetDateTimeOffset().UtcDateTime);

        var from = Uri.EscapeDataString(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.FromHours(2)).ToString("O"));
        var to = Uri.EscapeDataString(new DateTimeOffset(2026, 9, 30, 23, 59, 59, TimeSpan.FromHours(2)).ToString("O"));
        var report = await alice.GetJsonAsync($"/api/reports?from={from}&to={to}&timeZone=Europe/Madrid");
        Assert.Equal(5, report.GetProperty("totalExpenses").GetDecimal());
    }

    private static Task WaitReadyAsync(HttpClient client, Guid walletId, decimal balance)
        => EventuallyAsync(async () =>
        {
            var wallet = await client.GetJsonAsync($"/api/wallets/{walletId}");
            return wallet.GetProperty("isReady").GetBoolean() && wallet.GetProperty("balance").GetDecimal() == balance
                ? (object)true
                : null;
        }, "the wallet to be ready");

    private static StringContent Json(object body)
        => new(JsonSerializer.Serialize(body, ApiHelpers.Json), System.Text.Encoding.UTF8, "application/json");

    private static async Task<HttpResponseMessage> Put(HttpClient client, string path, object body, bool expectSuccess = true)
    {
        HttpResponseMessage response = null!;

        for (var attempt = 0; attempt < 20; attempt++)
        {
            response = await client.PutAsync(path, Json(body));
            if (response.StatusCode != HttpStatusCode.ServiceUnavailable) break;
            await Task.Delay(300);
        }

        if (expectSuccess && !response.IsSuccessStatusCode)
            throw new InvalidOperationException($"PUT {path}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");

        return response;
    }
}
