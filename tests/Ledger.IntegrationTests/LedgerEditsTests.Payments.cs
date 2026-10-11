using System.Net;
using System.Text.Json;
using static Ledger.IntegrationTests.ApiHelpers;

namespace Ledger.IntegrationTests;

/// <summary>Partial payments: oldest debt first, mutual debts, overpayments, edits and deletes.</summary>
public partial class LedgerEditsTests
{
    [Fact]
    public async Task A_payment_pays_the_oldest_debt_first_and_can_be_edited_and_deleted()
    {
        var alice = factory.ClientFor(NewUser("alice"));
        await alice.GetJsonAsync("/api/me");
        var bank = await alice.CreateReadyWalletAsync("Bank", "EUR", 1000);
        var brother = (await alice.PostJsonAsync("/api/contacts", new { name = "Brother" })).GetProperty("id").GetGuid();

        // My brother owes me 150 (oldest) and 50.
        var oldLoan = await alice.PostJsonAsync("/api/debts/loans",
            new { contactId = brother, walletId = bank, amount = 150, occurredAt = DateTimeOffset.UtcNow.AddDays(-10) }, HttpStatusCode.Accepted);
        await alice.CompletedOperationAsync(oldLoan);
        var newLoan = await alice.PostJsonAsync("/api/debts/loans",
            new { contactId = brother, walletId = bank, amount = 50, occurredAt = DateTimeOffset.UtcNow.AddDays(-5) }, HttpStatusCode.Accepted);
        await alice.CompletedOperationAsync(newLoan);
        await BalanceAsync(alice, bank, 800);

        var preview = await alice.PostJsonAsync("/api/debts/payments/preview",
            new { contactId = brother, currency = "EUR", direction = "TheyPaidMe", amount = 170 }, HttpStatusCode.OK);
        Assert.Equal([(150m, 0m), (20m, 30m)], preview.GetProperty("lines").EnumerateArray()
            .Select(l => (l.GetProperty("paid").GetDecimal(), l.GetProperty("remainingAfter").GetDecimal())));
        Assert.Equal(200, preview.GetProperty("balanceBefore").GetDecimal());
        Assert.Equal(30, preview.GetProperty("balanceAfter").GetDecimal());

        // He pays me 170: the oldest is settled (history only), the newest partially paid.
        var payment = await alice.PostJsonAsync("/api/debts/payments",
            new { contactId = brother, currency = "EUR", direction = "TheyPaidMe", amount = 170, walletId = bank, occurredAt = DateTimeOffset.UtcNow.AddDays(-1) },
            HttpStatusCode.Accepted);
        Assert.Equal("Completed", (await alice.CompletedOperationAsync(payment)).GetProperty("status").GetString());
        var paymentId = payment.GetProperty("id").GetGuid();
        await BalanceAsync(alice, bank, 970);

        var open = (await alice.GetJsonAsync("/api/debts")).GetProperty("debts").EnumerateArray().ToList();
        var partial = Assert.Single(open);
        Assert.Equal((50m, 20m, 30m, true), (
            partial.GetProperty("amount").GetDecimal(),
            partial.GetProperty("paid").GetDecimal(),
            partial.GetProperty("remaining").GetDecimal(),
            partial.GetProperty("isPartiallyPaid").GetBoolean()));
        Assert.Equal(30, (await alice.GetJsonAsync("/api/debts")).GetProperty("byContact").EnumerateArray().Single().GetProperty("net").GetDecimal());
        Assert.Equal(30, (await alice.TotalsAsync("EUR")).GetProperty("owedToMe").GetDecimal());

        var settled = (await alice.GetJsonAsync("/api/debts?includeSettled=true")).GetProperty("debts").EnumerateArray()
            .Single(d => d.GetProperty("status").GetString() == "Settled");
        Assert.Equal(150, settled.GetProperty("amount").GetDecimal());

        var paymentsOfPartial = (await alice.GetJsonAsync($"/api/debts/{partial.GetProperty("id").GetGuid()}/payments")).EnumerateArray().ToList();
        Assert.Equal(20, Assert.Single(paymentsOfPartial).GetProperty("amount").GetDecimal());

        var details = await alice.GetJsonAsync($"/api/debts/operations/{paymentId}");
        Assert.Equal("Payment", details.GetProperty("scope").GetString());
        Assert.Equal([150m, 20m], details.GetProperty("allocations").EnumerateArray().Select(a => a.GetProperty("amount").GetDecimal()));

        // The paid loans can't change until the payment does.
        var oldLoanId = oldLoan.GetProperty("id").GetGuid();
        Assert.Equal("debt.settled", (await alice.GetJsonAsync($"/api/debts/operations/{oldLoanId}")).GetProperty("blockedReason").GetString());
        await AssertRefusedAsync(Raw(alice, HttpMethod.Put, $"/api/debts/operations/{oldLoanId}",
            new { amount = 100, occurredAt = DateTimeOffset.UtcNow.AddDays(-10), reason = "x" }), "debt.settled");

        // He actually paid 130: the oldest is partially paid and the newest is untouched.
        await Send(alice, HttpMethod.Put, $"/api/debts/operations/{paymentId}",
            new { amount = 130, occurredAt = DateTimeOffset.UtcNow.AddDays(-1), walletId = bank, reason = "It was 130" });
        var v2 = await WaitAppliedAsync(alice, paymentId);
        await BalanceAsync(alice, bank, 930);

        await EventuallyAsync(async () => (await alice.TotalsAsync("EUR")).GetProperty("owedToMe").GetDecimal() == 70 ? (object)true : null, "owed 70");
        var afterEdit = (await alice.GetJsonAsync("/api/debts")).GetProperty("debts").EnumerateArray()
            .OrderBy(d => d.GetProperty("occurredAt").GetDateTimeOffset())
            .Select(d => (d.GetProperty("amount").GetDecimal(), d.GetProperty("remaining").GetDecimal()))
            .ToList();
        Assert.Equal([(150m, 20m), (50m, 50m)], afterEdit);

        // Deleting it reopens everything.
        await Send(alice, HttpMethod.Delete, $"/api/debts/operations/{v2}", new { reason = "He did not pay" });
        await WaitAppliedAsync(alice, v2);
        await BalanceAsync(alice, bank, 800);
        Assert.Equal(200, (await alice.TotalsAsync("EUR")).GetProperty("owedToMe").GetDecimal());
        Assert.All((await alice.GetJsonAsync("/api/debts")).GetProperty("debts").EnumerateArray(),
            d => Assert.Equal(0, d.GetProperty("paid").GetDecimal()));
        Assert.Equal(JsonValueKind.Null, (await alice.GetJsonAsync($"/api/debts/operations/{oldLoanId}")).GetProperty("blockedReason").ValueKind);
    }

    [Fact]
    public async Task Mutual_debts_cancel_out_an_overpayment_becomes_a_debt_and_a_linked_user_confirms_the_edit()
    {
        var (alice, bob, aliceId, _, bobForAlice) = await LinkedPairAsync();
        var aliceBank = await alice.CreateReadyWalletAsync("Bank", "EUR", 100);
        var bobBank = await bob.CreateReadyWalletAsync("Bank", "EUR", 100);

        // Bob owes Alice 40 (loan); Alice owes Bob 10 (from before Ledger).
        var loan = await EventuallyAsync<JsonElement?>(async () =>
        {
            var response = await Raw(alice, HttpMethod.Post, "/api/debts/loans",
                new { contactId = bobForAlice, walletId = aliceBank, amount = 40, occurredAt = DateTimeOffset.UtcNow.AddDays(-5) });
            return response.StatusCode == HttpStatusCode.Accepted ? await ReadAsync(response) : null;
        }, "the loan to be accepted");
        await alice.CompletedOperationAsync(loan!.Value);
        var initial = await alice.PostJsonAsync("/api/debts/initial",
            new { contactId = bobForAlice, direction = "IOwe", amount = 10, currency = "EUR", occurredAt = DateTimeOffset.UtcNow.AddDays(-4) },
            HttpStatusCode.Accepted);
        await alice.CompletedOperationAsync(initial);
        await EventuallyAsync(async () => (await bob.TotalsAsync("EUR")).GetProperty("iOwe").GetDecimal() == 40 ? (object)true : null, "bob's debts");

        var aliceForBob = await EventuallyAsync(async () =>
            (await bob.GetJsonAsync("/api/contacts")).EnumerateArray()
                .Where(c => c.GetProperty("personId").GetGuid() == aliceId)
                .Select(c => (object?)c.GetProperty("id").GetGuid())
                .FirstOrDefault(), "Bob's contact for Alice");

        // Bob pays Alice 80: the 10 cancel out, 30 pay the rest of the loan, 50 are extra: Alice now owes Bob 50.
        var payment = await bob.PostJsonAsync("/api/debts/payments",
            new { contactId = (Guid)aliceForBob, currency = "EUR", direction = "IPaidThem", amount = 80, walletId = bobBank },
            HttpStatusCode.Accepted);
        Assert.Equal("Completed", (await bob.CompletedOperationAsync(payment)).GetProperty("status").GetString());
        var paymentId = payment.GetProperty("id").GetGuid();

        await BalanceAsync(bob, bobBank, 20);
        var bobTotals = await bob.TotalsAsync("EUR");
        Assert.Equal((50m, 0m), (bobTotals.GetProperty("owedToMe").GetDecimal(), bobTotals.GetProperty("iOwe").GetDecimal()));
        await EventuallyAsync(async () => (await alice.TotalsAsync("EUR")).GetProperty("iOwe").GetDecimal() == 50 ? (object)true : null, "Alice owes the extra");
        Assert.Equal(0, (await alice.TotalsAsync("EUR")).GetProperty("owedToMe").GetDecimal());

        var details = await bob.GetJsonAsync($"/api/debts/operations/{paymentId}");
        Assert.Equal(50, details.GetProperty("extraAmount").GetDecimal());
        Assert.Equal(2, details.GetProperty("allocations").GetArrayLength());

        // Only Bob (who recorded it) may change the amount.
        await AssertRefusedAsync(Raw(alice, HttpMethod.Put, $"/api/debts/operations/{paymentId}",
            new { amount = 30, occurredAt = DateTimeOffset.UtcNow, reason = "x" }), "operation.initiator_only");

        // Bob corrects it to 30 and Alice accepts: everything is even, the extra debt is gone.
        var change = await Send(bob, HttpMethod.Put, $"/api/debts/operations/{paymentId}",
            new { amount = 30, occurredAt = DateTimeOffset.UtcNow, walletId = bobBank, reason = "It was 30" });
        Assert.Equal("Pending", change.GetProperty("status").GetString());
        var toReview = (await alice.GetJsonAsync("/api/debts/changes")).EnumerateArray().Single();
        await Send(alice, HttpMethod.Post, $"/api/debts/changes/{toReview.GetProperty("id").GetGuid()}/accept", new { });
        await WaitAppliedAsync(bob, paymentId);

        await BalanceAsync(bob, bobBank, 70);
        await EventuallyAsync(async () =>
        {
            var a = await alice.TotalsAsync("EUR");
            return a.GetProperty("iOwe").GetDecimal() == 0 && a.GetProperty("owedToMe").GetDecimal() == 0 ? (object)true : null;
        }, "Alice even");
        bobTotals = await bob.TotalsAsync("EUR");
        Assert.Equal((0m, 0m), (bobTotals.GetProperty("owedToMe").GetDecimal(), bobTotals.GetProperty("iOwe").GetDecimal()));
        Assert.Empty((await bob.GetJsonAsync("/api/debts")).GetProperty("debts").EnumerateArray());
    }
}
