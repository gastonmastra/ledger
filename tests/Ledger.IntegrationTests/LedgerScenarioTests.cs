using System.Net;
using System.Text.Json;
using static Ledger.IntegrationTests.ApiHelpers;

namespace Ledger.IntegrationTests;

[Collection(LedgerCollection.Name)]
public class LedgerScenarioTests(LedgerApiFactory factory)
{
    private static string NewUser(string name) => $"{name}-{Guid.NewGuid():N}"[..20];

    [Fact]
    public async Task Transfers_move_money_between_wallets_and_never_overdraw()
    {
        var alice = factory.ClientFor(NewUser("alice"));
        await alice.GetJsonAsync("/api/me");

        var bank = await alice.CreateReadyWalletAsync("Bank", "EUR", 100);
        var cash = await alice.CreateReadyWalletAsync("Cash", "EUR", 0);
        var dollars = await alice.CreateReadyWalletAsync("Dollars", "USD", 50);

        await alice.PostJsonAsync("/api/transactions/transfer", new { fromWalletId = bank, toWalletId = cash, amount = 40 });
        await alice.PostJsonAsync("/api/transactions/transfer",
            new { fromWalletId = dollars, toWalletId = bank, amount = 10, receivedAmount = 9.2m });

        var overdraft = await alice.PostJsonAsync("/api/transactions/transfer",
            new { fromWalletId = cash, toWalletId = bank, amount = 40.01m }, HttpStatusCode.BadRequest);
        Assert.Equal("ledger.insufficient_funds", overdraft.GetProperty("code").GetString());

        await EventuallyAsync(async () => await alice.WalletBalanceAsync(bank) == 69.2m ? (object)true : null, "bank balance");
        Assert.Equal(40, await alice.WalletBalanceAsync(cash));

        var eur = await alice.TotalsAsync("EUR");
        Assert.Equal(109.2m, eur.GetProperty("wallets").GetDecimal());
        Assert.Equal(40, (await alice.TotalsAsync("USD")).GetProperty("net").GetDecimal());

        // Newest first, filtered by wallet: opening balance of Cash is not there (it had none).
        var history = await alice.GetJsonAsync($"/api/transactions?walletId={cash}");
        Assert.Equal(1, history.GetProperty("totalCount").GetInt32());
        Assert.Equal("Transfer", history.GetProperty("items")[0].GetProperty("kind").GetString());
    }

    [Fact]
    public async Task A_split_with_a_linked_user_shows_up_in_both_ledgers_and_can_be_settled()
    {
        var aliceName = NewUser("alice");
        var bobName = NewUser("bob");
        var alice = factory.ClientFor(aliceName);
        var bob = factory.ClientFor(bobName);
        var aliceId = (await alice.GetJsonAsync("/api/me")).GetProperty("personId").GetGuid();
        var bobId = (await bob.GetJsonAsync("/api/me")).GetProperty("personId").GetGuid();

        var aliceBank = await alice.CreateReadyWalletAsync("Bank", "EUR", 100);
        var bobBank = await bob.CreateReadyWalletAsync("Bank", "EUR", 50);

        // Link Bob by email (his registration may still be propagating to the Contacts module).
        var contact = await alice.PostJsonAsync("/api/contacts", new { name = "Bob" });
        var contactId = contact.GetProperty("id").GetGuid();
        await EventuallyAsync(async () =>
        {
            var response = await alice.PostAsync(
                $"/api/contacts/{contactId}/link",
                JsonContent(new { email = $"{bobName}@test.local" }));
            return response.IsSuccessStatusCode ? (object)true : null;
        }, "Bob to be linkable");

        // The Debts module learns about the link asynchronously: retry until it knows Bob.
        var split = await EventuallyAsync<JsonElement?>(async () =>
        {
            var response = await alice.PostAsync("/api/debts/splits", JsonContent(new
            {
                total = 60,
                currency = "EUR",
                method = "Equal",
                description = "Dinner",
                payers = new[] { new { personId = aliceId, amount = 60, walletId = (Guid?)aliceBank } },
                participants = new[] { new { personId = aliceId }, new { personId = bobId } }
            }));

            return response.StatusCode == HttpStatusCode.Accepted
                ? JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync())
                : null;
        }, "the split to be accepted");

        Assert.Equal("Completed", (await alice.CompletedOperationAsync(split!.Value)).GetProperty("status").GetString());

        var aliceEur = await alice.TotalsAsync("EUR");
        Assert.Equal(40, aliceEur.GetProperty("wallets").GetDecimal());
        Assert.Equal(30, aliceEur.GetProperty("owedToMe").GetDecimal());

        // Mirrored automatically on Bob's side.
        var bobEur = await bob.TotalsAsync("EUR");
        Assert.Equal(30, bobEur.GetProperty("iOwe").GetDecimal());
        var aliceForBob = (await EventuallyAsync(async () =>
            (await bob.GetJsonAsync("/api/contacts")).EnumerateArray()
                .Cast<JsonElement?>()
                .FirstOrDefault(c => c!.Value.GetProperty("personId").GetGuid() == aliceId), "Alice in Bob's contacts")).GetValueOrDefault();
        Assert.Equal("Mirrored", aliceForBob.GetProperty("origin").GetString());

        // Bob settles up; the money reaches Alice's pending list until she picks a wallet.
        var settlement = await bob.PostJsonAsync("/api/debts/settlements", new
        {
            scope = "Contact",
            contactId = aliceForBob.GetProperty("id").GetGuid(),
            currency = "EUR",
            walletId = bobBank
        }, HttpStatusCode.Accepted);
        Assert.Equal("Completed", (await bob.CompletedOperationAsync(settlement)).GetProperty("status").GetString());

        Assert.Equal(20, await bob.WalletBalanceAsync(bobBank));
        Assert.Equal(0, (await bob.TotalsAsync("EUR")).GetProperty("iOwe").GetDecimal());

        var pending = (await alice.GetJsonAsync("/api/pending-assignments")).EnumerateArray().Single();
        Assert.Equal(30, pending.GetProperty("amount").GetDecimal());

        await alice.PostJsonAsync($"/api/pending-assignments/{pending.GetProperty("id").GetGuid()}/assign",
            new { walletId = aliceBank }, HttpStatusCode.OK);

        await EventuallyAsync(async () => await alice.WalletBalanceAsync(aliceBank) == 70 ? (object)true : null, "alice bank");
        aliceEur = await alice.TotalsAsync("EUR");
        Assert.Equal(0, aliceEur.GetProperty("owedToMe").GetDecimal());
        Assert.Equal(70, aliceEur.GetProperty("net").GetDecimal());
    }

    [Fact]
    public async Task A_loan_larger_than_the_wallet_is_rejected()
    {
        var alice = factory.ClientFor(NewUser("alice"));
        await alice.GetJsonAsync("/api/me");
        var wallet = await alice.CreateReadyWalletAsync("Pocket", "EUR", 10);
        var carl = await alice.PostJsonAsync("/api/contacts", new { name = "Carl" });

        var loan = await alice.PostJsonAsync("/api/debts/loans",
            new { contactId = carl.GetProperty("id").GetGuid(), walletId = wallet, amount = 50 }, HttpStatusCode.Accepted);

        var result = await alice.CompletedOperationAsync(loan);
        Assert.Equal("Rejected", result.GetProperty("status").GetString());
        Assert.Contains("not have enough money", result.GetProperty("rejectionReason").GetString());

        Assert.Empty((await alice.GetJsonAsync("/api/debts")).GetProperty("debts").EnumerateArray());
        Assert.Equal(10, await alice.WalletBalanceAsync(wallet));

        var smallLoan = await alice.PostJsonAsync("/api/debts/loans",
            new { contactId = carl.GetProperty("id").GetGuid(), walletId = wallet, amount = 4 }, HttpStatusCode.Accepted);
        Assert.Equal("Completed", (await alice.CompletedOperationAsync(smallLoan)).GetProperty("status").GetString());
        Assert.Equal(4, (await alice.TotalsAsync("EUR")).GetProperty("owedToMe").GetDecimal());
    }

    [Fact]
    public async Task Group_balances_are_simplified_and_settled_pair_by_pair()
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

        // Alice paid 80, Bob paid 40 of a 120 hotel shared by the three of them: only Carl owes (40, to Alice).
        var split = await alice.PostJsonAsync("/api/debts/splits", new
        {
            groupId,
            total = 120,
            currency = "EUR",
            method = "Equal",
            description = "Hotel",
            payers = new object[] { new { personId = aliceId, amount = 80, walletId = bank }, new { personId = bobId, amount = 40 } },
            participants = new[] { new { personId = aliceId }, new { personId = bobId }, new { personId = carlId } }
        }, HttpStatusCode.Accepted);
        Assert.Equal("Completed", (await alice.CompletedOperationAsync(split)).GetProperty("status").GetString());

        var balances = await alice.GetJsonAsync($"/api/groups/{groupId}/balances");
        var suggestion = balances.GetProperty("suggestedPayments").EnumerateArray().Single();
        Assert.Equal((carlId, aliceId, 40m), (
            suggestion.GetProperty("fromPersonId").GetGuid(),
            suggestion.GetProperty("toPersonId").GetGuid(),
            suggestion.GetProperty("amount").GetDecimal()));

        var settle = await alice.PostJsonAsync("/api/debts/settlements", new
        {
            scope = "GroupPair",
            groupId,
            fromPersonId = carlId,
            toPersonId = aliceId,
            currency = "EUR",
            walletId = bank
        }, HttpStatusCode.Accepted);
        Assert.Equal("Completed", (await alice.CompletedOperationAsync(settle)).GetProperty("status").GetString());

        balances = await alice.GetJsonAsync($"/api/groups/{groupId}/balances");
        Assert.Empty(balances.GetProperty("suggestedPayments").EnumerateArray());
        Assert.All(balances.GetProperty("balances").EnumerateArray(), b => Assert.Equal(0, b.GetProperty("balance").GetDecimal()));

        await EventuallyAsync(async () => await alice.WalletBalanceAsync(bank) == 160 ? (object)true : null, "bank balance");
        Assert.Equal(0, (await alice.TotalsAsync("EUR")).GetProperty("owedToMe").GetDecimal());
    }

    [Fact]
    public async Task Requests_without_a_token_are_rejected_with_401()
    {
        var anonymous = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/wallets")).StatusCode);
    }

    [Fact]
    public async Task A_wallet_not_known_yet_by_the_debts_module_is_retryable()
    {
        var alice = factory.ClientFor(NewUser("alice"));
        await alice.GetJsonAsync("/api/me");
        var carl = await alice.PostJsonAsync("/api/contacts", new { name = "Carl" });

        // Wait until Debts knows the contact, so the only unknown thing is the wallet.
        var response = await EventuallyAsync(async () =>
        {
            var attempt = await alice.PostAsync("/api/debts/loans", JsonContent(new
            {
                contactId = carl.GetProperty("id").GetGuid(),
                walletId = Guid.NewGuid(),
                amount = 10
            }));
            var body = await attempt.Content.ReadAsStringAsync();
            return body.Contains("Wallet") ? attempt : null;
        }, "the contact to reach the Debts module");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.True(response.Headers.Contains("Retry-After"));
    }

    private static StringContent JsonContent(object body)
        => new(JsonSerializer.Serialize(body, Json), System.Text.Encoding.UTF8, "application/json");
}
