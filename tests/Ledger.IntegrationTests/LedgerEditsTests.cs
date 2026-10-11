using System.Net;
using System.Text.Json;
using static Ledger.IntegrationTests.ApiHelpers;

namespace Ledger.IntegrationTests;

/// <summary>Edits, deletions, restores, history, lock dates and confirmations of shared changes.</summary>
[Collection(LedgerCollection.Name)]
public partial class LedgerEditsTests(LedgerApiFactory factory)
{
    private static string NewUser(string name) => $"{name}-{Guid.NewGuid():N}"[..20];

    [Fact]
    public async Task An_expense_can_be_edited_deleted_and_restored_and_is_counted_once()
    {
        var alice = factory.ClientFor(NewUser("alice"));
        await alice.GetJsonAsync("/api/me");
        var bank = await alice.CreateReadyWalletAsync("Bank", "EUR", 100);
        var when = DateTimeOffset.UtcNow.AddDays(-10);

        var created = await alice.PostJsonAsync("/api/transactions/expense", new { walletId = bank, amount = 30, occurredAt = when });
        var expenseId = created.GetProperty("id").GetGuid();

        var edited = await Send(alice, HttpMethod.Put, $"/api/transactions/{expenseId}",
            new { walletId = bank, amount = 50, occurredAt = when, description = "Groceries", reason = "Typo in the amount" });
        var currentId = edited.GetProperty("id").GetGuid();

        await BalanceAsync(alice, bank, 50);
        Assert.Equal(50, (await ReportAsync(alice)).GetProperty("totalExpenses").GetDecimal());

        var list = (await alice.GetJsonAsync($"/api/transactions?walletId={bank}&pageSize=50")).GetProperty("items").EnumerateArray().ToList();
        var current = Assert.Single(list, t => t.GetProperty("kind").GetString() == "Expense");
        Assert.Equal(currentId, current.GetProperty("id").GetGuid());
        Assert.Equal(2, current.GetProperty("revision").GetInt32());

        var history = (await alice.GetJsonAsync($"/api/transactions/{currentId}/history")).EnumerateArray().ToList();
        Assert.Equal(new[] { "Changed", "Reversal", "Current" }, history.Select(h => h.GetProperty("status").GetString()));
        Assert.Equal("Typo in the amount", history[1].GetProperty("reason").GetString());

        // A correction that would overdraw the wallet is refused and nothing changes.
        var tooMuch = await Raw(alice, HttpMethod.Put, $"/api/transactions/{currentId}",
            new { walletId = bank, amount = 500, occurredAt = when, reason = "More" });
        Assert.Equal(HttpStatusCode.BadRequest, tooMuch.StatusCode);
        await BalanceAsync(alice, bank, 50);

        var deleted = await Raw(alice, HttpMethod.Delete, $"/api/transactions/{currentId}", new { reason = "Duplicated" });
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        await BalanceAsync(alice, bank, 100);
        Assert.Equal(0, (await ReportAsync(alice)).GetProperty("totalExpenses").GetDecimal());
        Assert.DoesNotContain((await alice.GetJsonAsync($"/api/transactions?walletId={bank}")).GetProperty("items").EnumerateArray(),
            t => t.GetProperty("kind").GetString() == "Expense");

        var restored = await Send(alice, HttpMethod.Post, $"/api/transactions/{currentId}/restore", new { reason = "It was not duplicated" });
        await BalanceAsync(alice, bank, 50);
        Assert.Equal(50, (await ReportAsync(alice)).GetProperty("totalExpenses").GetDecimal());
        // Original, its reversal, the correction, its reversal (deleted) and the restored copy.
        Assert.Equal(5, (await alice.GetJsonAsync($"/api/transactions/{restored.GetProperty("id").GetGuid()}/history")).GetArrayLength());

        var activity = (await alice.GetJsonAsync("/api/transactions/activity")).GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(new[] { "Restore", "Delete", "Edit" }, activity.Select(a => a.GetProperty("action").GetString()));
    }

    [Fact]
    public async Task The_lock_date_blocks_changes_in_closed_periods()
    {
        var alice = factory.ClientFor(NewUser("alice"));
        await alice.GetJsonAsync("/api/me");
        var bank = await alice.CreateReadyWalletAsync("Bank", "EUR", 100);
        var old = DateTimeOffset.UtcNow.AddDays(-10);

        var expense = (await alice.PostJsonAsync("/api/transactions/expense", new { walletId = bank, amount = 10, occurredAt = old }))
            .GetProperty("id").GetGuid();

        var locked = await Send(alice, HttpMethod.Put, "/api/settings/books-lock", new { lockedUntil = DateTimeOffset.UtcNow.AddDays(-5) });
        Assert.NotEqual(JsonValueKind.Null, locked.GetProperty("lockedUntil").ValueKind);

        await AssertRefusedAsync(
            Raw(alice, HttpMethod.Post, "/api/transactions/expense", new { walletId = bank, amount = 5, occurredAt = old }), "books.locked");
        await AssertRefusedAsync(
            Raw(alice, HttpMethod.Put, $"/api/transactions/{expense}", new { walletId = bank, amount = 20, occurredAt = old, reason = "x" }),
            "books.locked");
        await AssertRefusedAsync(Raw(alice, HttpMethod.Delete, $"/api/transactions/{expense}", new { reason = "x" }), "books.locked");

        // After the lock date everything works as usual; moving it back reopens the period.
        await alice.PostJsonAsync("/api/transactions/expense", new { walletId = bank, amount = 5, occurredAt = DateTimeOffset.UtcNow.AddDays(-1) });
        await Send(alice, HttpMethod.Put, "/api/settings/books-lock", new { lockedUntil = (DateTimeOffset?)null });
        Assert.Equal(HttpStatusCode.NoContent, (await Raw(alice, HttpMethod.Delete, $"/api/transactions/{expense}", new { reason = "x" })).StatusCode);
        await BalanceAsync(alice, bank, 95);
    }

    [Fact]
    public async Task A_shared_loan_change_waits_for_the_linked_user_and_can_be_disputed()
    {
        var (alice, bob, _, bobId, contactId) = await LinkedPairAsync();
        var aliceBank = await alice.CreateReadyWalletAsync("Bank", "EUR", 100);
        var bobBank = await bob.CreateReadyWalletAsync("Bank", "EUR", 100);
        var when = DateTimeOffset.UtcNow.AddDays(-3);

        var loan = await EventuallyAsync<JsonElement?>(async () =>
        {
            var response = await Raw(alice, HttpMethod.Post, "/api/debts/loans",
                new { contactId, walletId = aliceBank, amount = 40, occurredAt = when });
            return response.StatusCode == HttpStatusCode.Accepted ? await ReadAsync(response) : null;
        }, "the loan to be accepted");
        var loanId = loan!.Value.GetProperty("id").GetGuid();
        Assert.Equal("Completed", (await alice.CompletedOperationAsync(loan.Value)).GetProperty("status").GetString());
        await EventuallyAsync(async () => (await bob.TotalsAsync("EUR")).GetProperty("iOwe").GetDecimal() == 40 ? (object)true : null, "bob's debt");

        // 1) Alice raises it to 60: nothing changes until Bob answers.
        var change = await Send(alice, HttpMethod.Put, $"/api/debts/operations/{loanId}",
            new { amount = 60, occurredAt = when, reason = "I lent him 60" });
        Assert.Equal("Pending", change.GetProperty("status").GetString());
        Assert.Single(change.GetProperty("waitingFor").EnumerateArray());

        var details = await alice.GetJsonAsync($"/api/debts/operations/{loanId}");
        Assert.NotEqual(JsonValueKind.Null, details.GetProperty("pendingChange").ValueKind);
        Assert.Equal(40, (await alice.TotalsAsync("EUR")).GetProperty("owedToMe").GetDecimal());

        // Paying it is blocked meanwhile.
        var bobDebt = (await bob.GetJsonAsync("/api/debts")).GetProperty("debts").EnumerateArray().Single();
        Assert.True(bobDebt.GetProperty("pendingChange").GetBoolean());
        await AssertRefusedAsync(Raw(alice, HttpMethod.Post, "/api/debts/payments",
            new { contactId, currency = "EUR", direction = "TheyPaidMe", amount = 10, walletId = aliceBank }), "operation.change_pending");

        var toReview = (await bob.GetJsonAsync("/api/debts/changes")).EnumerateArray().Single();
        Assert.Equal(40, toReview.GetProperty("current").GetProperty("total").GetDecimal());
        Assert.Equal(60, toReview.GetProperty("proposed").GetProperty("total").GetDecimal());
        await Send(bob, HttpMethod.Post, $"/api/debts/changes/{toReview.GetProperty("id").GetGuid()}/accept", new { });

        var v2 = await WaitAppliedAsync(alice, loanId);
        await EventuallyAsync(async () => (await bob.TotalsAsync("EUR")).GetProperty("iOwe").GetDecimal() == 60 ? (object)true : null, "bob's new debt");
        Assert.Equal(60, (await alice.TotalsAsync("EUR")).GetProperty("owedToMe").GetDecimal());
        await BalanceAsync(alice, aliceBank, 40);
        Assert.Equal(60, (await alice.GetJsonAsync("/api/debts")).GetProperty("debts").EnumerateArray().Single().GetProperty("amount").GetDecimal());

        // 2) Bob edits it to 50, Alice rejects: Bob's books change, Alice's keep 60 and it is disputed.
        var second = await Send(bob, HttpMethod.Put, $"/api/debts/operations/{v2}",
            new { amount = 50, occurredAt = when, reason = "I paid 10 back in cash" });
        Assert.Equal("Pending", second.GetProperty("status").GetString());
        var aliceReview = (await alice.GetJsonAsync("/api/debts/changes")).EnumerateArray().Single();
        await Send(alice, HttpMethod.Post, $"/api/debts/changes/{aliceReview.GetProperty("id").GetGuid()}/reject", new { reason = "No, you did not" });

        var v3 = await WaitAppliedAsync(bob, v2);
        Assert.True((await bob.GetJsonAsync($"/api/debts/operations/{v3}")).GetProperty("isDisputed").GetBoolean());
        await EventuallyAsync(async () => (await bob.TotalsAsync("EUR")).GetProperty("iOwe").GetDecimal() == 50 ? (object)true : null, "bob's books");
        Assert.Equal(60, (await alice.TotalsAsync("EUR")).GetProperty("owedToMe").GetDecimal());

        var history = await alice.GetJsonAsync($"/api/debts/operations/{v3}/history");
        Assert.Equal(3, history.GetProperty("versions").GetArrayLength());
        Assert.Equal(2, history.GetProperty("changes").GetArrayLength());

        // 3) Bob closes his books: a change that touches his closed period is refused for Alice.
        await Send(bob, HttpMethod.Put, "/api/settings/books-lock", new { lockedUntil = DateTimeOffset.UtcNow.AddMinutes(-1) });
        await EventuallyAsync(async () =>
        {
            var response = await Raw(alice, HttpMethod.Put, $"/api/debts/operations/{v3}",
                new { amount = 55, occurredAt = when, reason = "Meet halfway" });
            return (await response.Content.ReadAsStringAsync()).Contains("books.locked_for_other") ? (object)true : null;
        }, "bob's lock to reach the Debts module");
    }

    [Fact]
    public async Task A_settled_debt_cannot_change_until_the_settlement_is_undone()
    {
        var alice = factory.ClientFor(NewUser("alice"));
        await alice.GetJsonAsync("/api/me");
        var bank = await alice.CreateReadyWalletAsync("Bank", "EUR", 100);
        var carl = await alice.PostJsonAsync("/api/contacts", new { name = "Carl" });
        var when = DateTimeOffset.UtcNow.AddDays(-2);

        var loan = await alice.PostJsonAsync("/api/debts/loans",
            new { contactId = carl.GetProperty("id").GetGuid(), walletId = bank, amount = 20, occurredAt = when }, HttpStatusCode.Accepted);
        await alice.CompletedOperationAsync(loan);
        var loanId = loan.GetProperty("id").GetGuid();

        var settlement = await alice.PostJsonAsync("/api/debts/payments",
            new { contactId = carl.GetProperty("id").GetGuid(), currency = "EUR", direction = "TheyPaidMe", amount = 20, walletId = bank },
            HttpStatusCode.Accepted);
        await alice.CompletedOperationAsync(settlement);
        await EventuallyAsync(async () => await alice.WalletBalanceAsync(bank) == 100 ? (object)true : null, "paid back");

        var details = await alice.GetJsonAsync($"/api/debts/operations/{loanId}");
        Assert.Equal("debt.settled", details.GetProperty("blockedReason").GetString());
        Assert.False(details.GetProperty("canEdit").GetBoolean());
        await AssertRefusedAsync(Raw(alice, HttpMethod.Put, $"/api/debts/operations/{loanId}",
            new { amount = 10, occurredAt = when, reason = "x" }), "debt.settled");

        // Carl has no account: nobody has to confirm, it is applied at once.
        var undo = await Send(alice, HttpMethod.Delete, $"/api/debts/operations/{settlement.GetProperty("id").GetGuid()}",
            new { reason = "He did not pay yet" });
        Assert.NotEqual("Pending", undo.GetProperty("status").GetString());
        await EventuallyAsync(async () => (await alice.TotalsAsync("EUR")).GetProperty("owedToMe").GetDecimal() == 20 ? (object)true : null, "debt reopened");
        await BalanceAsync(alice, bank, 80);

        await Send(alice, HttpMethod.Put, $"/api/debts/operations/{loanId}", new { amount = 10, occurredAt = when, reason = "It was 10" });
        await WaitAppliedAsync(alice, loanId);
        await EventuallyAsync(async () => await alice.WalletBalanceAsync(bank) == 90 ? (object)true : null, "loan corrected");
        Assert.Equal(10, (await alice.TotalsAsync("EUR")).GetProperty("owedToMe").GetDecimal());
    }

    [Fact]
    public async Task An_existing_debt_is_neither_income_nor_expense_and_can_be_deleted_and_restored()
    {
        var alice = factory.ClientFor(NewUser("alice"));
        await alice.GetJsonAsync("/api/me");
        await alice.CreateReadyWalletAsync("Bank", "EUR", 100);
        var carl = await alice.PostJsonAsync("/api/contacts", new { name = "Carl" });
        var when = DateTimeOffset.UtcNow.AddDays(-2);

        var debt = await alice.PostJsonAsync("/api/debts/initial",
            new { contactId = carl.GetProperty("id").GetGuid(), direction = "TheyOweMe", amount = 50, currency = "EUR", occurredAt = when },
            HttpStatusCode.Accepted);
        await alice.CompletedOperationAsync(debt);
        var debtId = debt.GetProperty("id").GetGuid();

        Assert.Equal(50, (await alice.TotalsAsync("EUR")).GetProperty("owedToMe").GetDecimal());
        Assert.Equal(0, (await ReportAsync(alice)).GetProperty("totalIncome").GetDecimal());

        await Send(alice, HttpMethod.Put, $"/api/debts/operations/{debtId}",
            new { amount = 70, currency = "EUR", occurredAt = when, reason = "It was 70" });
        var v2 = await WaitAppliedAsync(alice, debtId);
        Assert.Equal(70, (await alice.TotalsAsync("EUR")).GetProperty("owedToMe").GetDecimal());
        Assert.Equal(0, (await ReportAsync(alice)).GetProperty("totalIncome").GetDecimal());

        await Send(alice, HttpMethod.Delete, $"/api/debts/operations/{v2}", new { reason = "Forgiven" });
        await WaitAppliedAsync(alice, v2);
        await EventuallyAsync(async () => (await alice.TotalsAsync("EUR")).GetProperty("owedToMe").GetDecimal() == 0 ? (object)true : null, "deleted");
        Assert.True((await alice.GetJsonAsync($"/api/debts/operations/{v2}")).GetProperty("canRestore").GetBoolean());

        await Send(alice, HttpMethod.Post, $"/api/debts/operations/{v2}/restore", new { reason = "Not forgiven after all" });
        await EventuallyAsync(async () => (await alice.TotalsAsync("EUR")).GetProperty("owedToMe").GetDecimal() == 70 ? (object)true : null, "restored");

        var activity = (await alice.GetJsonAsync("/api/debts/activity")).GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(new[] { "Restore", "Delete", "Revise" }, activity.Select(a => a.GetProperty("kind").GetString()));
    }

    [Fact]
    public async Task Editing_a_group_expense_recalculates_the_group_balances()
    {
        var alice = factory.ClientFor(NewUser("alice"));
        var aliceId = (await alice.GetJsonAsync("/api/me")).GetProperty("personId").GetGuid();
        var bank = await alice.CreateReadyWalletAsync("Bank", "EUR", 200);
        var bob = await alice.PostJsonAsync("/api/contacts", new { name = "Bob" });
        var carl = await alice.PostJsonAsync("/api/contacts", new { name = "Carl" });
        var group = await alice.PostJsonAsync("/api/groups", new
        {
            name = "Trip",
            contactIds = new[] { bob.GetProperty("id").GetGuid(), carl.GetProperty("id").GetGuid() }
        });
        var groupId = group.GetProperty("id").GetGuid();
        var bobId = bob.GetProperty("personId").GetGuid();
        var carlId = carl.GetProperty("personId").GetGuid();
        var when = DateTimeOffset.UtcNow.AddDays(-1);
        var participants = new[] { new { personId = aliceId }, new { personId = bobId }, new { personId = carlId } };

        var split = await alice.PostJsonAsync("/api/debts/splits", new
        {
            groupId, total = 120, currency = "EUR", method = "Equal", description = "Hotel", occurredAt = when,
            payers = new[] { new { personId = aliceId, amount = 120, walletId = bank } },
            participants
        }, HttpStatusCode.Accepted);
        await alice.CompletedOperationAsync(split);

        await Send(alice, HttpMethod.Put, $"/api/debts/operations/{split.GetProperty("id").GetGuid()}", new
        {
            amount = 90, currency = "EUR", method = "Equal", description = "Hotel (discount)", occurredAt = when,
            payers = new[] { new { personId = aliceId, amount = 90, walletId = bank } },
            participants,
            reason = "We got a discount"
        });
        await WaitAppliedAsync(alice, split.GetProperty("id").GetGuid());

        var balances = (await alice.GetJsonAsync($"/api/groups/{groupId}/balances")).GetProperty("balances").EnumerateArray()
            .ToDictionary(b => b.GetProperty("personId").GetGuid(), b => b.GetProperty("balance").GetDecimal());
        Assert.Equal(60, balances[aliceId]);
        Assert.Equal(-30, balances[bobId]);
        Assert.Equal(-30, balances[carlId]);
        await EventuallyAsync(async () => await alice.WalletBalanceAsync(bank) == 110 ? (object)true : null, "bank balance");

        var operations = (await alice.GetJsonAsync($"/api/debts/operations?groupId={groupId}")).GetProperty("items");
        Assert.Equal(90, operations.EnumerateArray().Single().GetProperty("total").GetDecimal());
    }

    /// <summary>Waits until the change of <paramref name="operationId"/> is applied; returns the new version's id (or the same id for a delete).</summary>
    private static async Task<Guid> WaitAppliedAsync(HttpClient client, Guid operationId)
        => await EventuallyAsync<Guid?>(async () =>
        {
            var history = await client.GetJsonAsync($"/api/debts/operations/{operationId}/history");
            var last = history.GetProperty("changes").EnumerateArray().LastOrDefault();

            if (last.ValueKind == JsonValueKind.Undefined)
                return null;

            return last.GetProperty("status").GetString() switch
            {
                "Applied" => last.GetProperty("newOperationId").ValueKind == JsonValueKind.Null
                    ? operationId
                    : last.GetProperty("newOperationId").GetGuid(),
                "Failed" => throw new InvalidOperationException($"The change failed: {last.GetProperty("failureReason")}"),
                _ => null
            };
        }, $"the change of {operationId}") ?? Guid.Empty;

    private async Task<(HttpClient Alice, HttpClient Bob, Guid AliceId, Guid BobId, Guid ContactId)> LinkedPairAsync()
    {
        var bobName = NewUser("bob");
        var alice = factory.ClientFor(NewUser("alice"));
        var bob = factory.ClientFor(bobName);
        var aliceId = (await alice.GetJsonAsync("/api/me")).GetProperty("personId").GetGuid();
        var bobId = (await bob.GetJsonAsync("/api/me")).GetProperty("personId").GetGuid();

        var contactId = (await alice.PostJsonAsync("/api/contacts", new { name = "Bob" })).GetProperty("id").GetGuid();
        await EventuallyAsync(async () =>
            (await Raw(alice, HttpMethod.Post, $"/api/contacts/{contactId}/link", new { email = $"{bobName}@test.local" }))
                .IsSuccessStatusCode ? (object)true : null, "Bob to be linkable");

        // The Debts module learns about the link asynchronously. Probe it with an invalid split (no
        // wallet): it fails with "unknown person" until the link arrives, then with "wallet required".
        await EventuallyAsync(async () =>
        {
            var probe = await Raw(alice, HttpMethod.Post, "/api/debts/splits", new
            {
                total = 1,
                currency = "EUR",
                method = "Equal",
                payers = new[] { new { personId = aliceId, amount = 1 } },
                participants = new[] { new { personId = aliceId }, new { personId = bobId } }
            });
            return (await probe.Content.ReadAsStringAsync()).Contains("split.wallet_required") ? (object)true : null;
        }, "the link to reach the Debts module");

        return (alice, bob, aliceId, bobId, contactId);
    }

    private static Task BalanceAsync(HttpClient client, Guid walletId, decimal expected)
        => EventuallyAsync(async () => await client.WalletBalanceAsync(walletId) == expected ? (object)true : null,
            $"the wallet balance to be {expected}");

    private static Task<JsonElement> ReportAsync(HttpClient client)
    {
        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-30).ToString("O"));
        var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.ToString("O"));
        return client.GetJsonAsync($"/api/reports?from={from}&to={to}&currency=EUR&timeZone=UTC");
    }

    private static async Task AssertRefusedAsync(Task<HttpResponseMessage> request, string code)
    {
        var response = await request;
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest && body.Contains(code), $"{(int)response.StatusCode}: {body}");
    }

    private static async Task<JsonElement> Send(HttpClient client, HttpMethod method, string path, object body)
    {
        var response = await Raw(client, method, path, body);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"{method} {path}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");

        return await ReadAsync(response);
    }

    /// <summary>Sends a JSON body; retries while something is still propagating between modules (503).</summary>
    private static async Task<HttpResponseMessage> Raw(HttpClient client, HttpMethod method, string path, object body)
    {
        for (var attempt = 1; ; attempt++)
        {
            var request = new HttpRequestMessage(method, path)
            {
                Content = new StringContent(JsonSerializer.Serialize(body, Json), System.Text.Encoding.UTF8, "application/json")
            };

            var response = await client.SendAsync(request);

            if (response.StatusCode != HttpStatusCode.ServiceUnavailable || attempt >= 30)
                return response;

            await Task.Delay(300);
        }
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return string.IsNullOrEmpty(text) ? default : JsonSerializer.Deserialize<JsonElement>(text, Json);
    }
}
