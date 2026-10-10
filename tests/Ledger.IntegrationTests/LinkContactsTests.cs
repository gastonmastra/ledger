using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static Ledger.IntegrationTests.ApiHelpers;

namespace Ledger.IntegrationTests;

/// <summary>Linking a contact that already has debts or groups to the account the person created later.</summary>
[Collection(LedgerCollection.Name)]
public class LinkContactsTests(LedgerApiFactory factory)
{
    private static string NewUser(string name) => $"{name}-{Guid.NewGuid():N}"[..20];

    [Fact]
    public async Task The_user_accepts_and_gets_the_shared_history_in_their_books()
    {
        var alice = factory.ClientFor(NewUser("alice"));
        await alice.GetJsonAsync("/api/me");
        var bank = await alice.CreateReadyWalletAsync("Bank", "EUR", 200);
        var pedroContact = (await alice.PostJsonAsync("/api/contacts", new { name = "Pedro" })).GetProperty("id").GetGuid();
        var aliceId = (await alice.GetJsonAsync("/api/me")).GetProperty("personId").GetGuid();
        var oldPersonId = await PersonIdOfAsync(alice, pedroContact);
        var loanDate = DateTimeOffset.UtcNow.AddDays(-5);

        var loan = await alice.PostJsonAsync("/api/debts/loans",
            new { contactId = pedroContact, walletId = bank, amount = 50, occurredAt = loanDate }, HttpStatusCode.Accepted);
        await alice.CompletedOperationAsync(loan);
        var dinner = await alice.PostJsonAsync("/api/debts/splits", new
        {
            total = 60, currency = "EUR", method = "Equal", description = "Dinner", occurredAt = DateTimeOffset.UtcNow.AddDays(-3),
            payers = new[] { new { personId = aliceId, amount = 60, walletId = bank } },
            participants = new[] { new { personId = aliceId }, new { personId = oldPersonId } }
        }, HttpStatusCode.Accepted);
        await alice.CompletedOperationAsync(dinner);

        // Pedro creates his account; Alice links her contact to it.
        var (pedro, pedroEmail, pedroId) = await NewAccountAsync("pedro");
        var request = await RequestLinkAsync(alice, pedroContact, pedroEmail);

        var incoming = (await pedro.GetJsonAsync("/api/contacts/link-requests")).EnumerateArray().Single();
        Assert.Equal("Incoming", incoming.GetProperty("direction").GetString());
        Assert.Equal("Pending", incoming.GetProperty("status").GetString());

        var preview = await PreviewAsync(pedro, request);
        Assert.Equal(2, preview.GetProperty("operations").GetInt32());
        var balance = preview.GetProperty("balances").EnumerateArray().Single();
        Assert.Equal(aliceId, balance.GetProperty("counterpartyId").GetGuid());
        Assert.Equal(-80, balance.GetProperty("amount").GetDecimal());

        await AcceptAsync(pedro, alice, request);

        // Pedro's books: he owes Alice 80, as existing debts dated like the operations; neither income nor expense.
        var pedroTotals = await EventuallyAsync(async () =>
        {
            var summary = await pedro.GetJsonAsync("/api/summary");
            var totals = summary.GetProperty("totals").EnumerateArray().SingleOrDefault(t => t.GetProperty("currency").GetString() == "EUR");
            return totals.ValueKind != JsonValueKind.Undefined && totals.GetProperty("iOwe").GetDecimal() == 80 ? (object)summary : null;
        }, "Pedro's books") as JsonElement?;
        var withAlice = pedroTotals!.Value.GetProperty("counterparties").EnumerateArray().Single();
        Assert.Equal(aliceId, withAlice.GetProperty("counterpartyId").GetGuid());

        var copies = (await pedro.GetJsonAsync("/api/transactions?pageSize=50")).GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, copies.Count);
        Assert.All(copies, t => Assert.Equal("InitialDebt", t.GetProperty("kind").GetString()));
        Assert.Contains(copies, t => Math.Abs((t.GetProperty("occurredAt").GetDateTimeOffset() - loanDate).TotalSeconds) < 1);

        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-30).ToString("O"));
        var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddMinutes(1).ToString("O"));
        var report = await pedro.GetJsonAsync($"/api/reports?from={from}&to={to}&currency=EUR&timeZone=UTC");
        Assert.Equal(0, report.GetProperty("totalIncome").GetDecimal());
        Assert.Equal(0, report.GetProperty("totalExpenses").GetDecimal());

        // Alice: the contact is now Pedro's account, and her old and new postings make one balance with him.
        await EventuallyAsync(async () =>
        {
            var contact = (await alice.GetJsonAsync("/api/contacts")).EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == pedroContact);
            return contact.GetProperty("isLinked").GetBoolean() && contact.GetProperty("personId").GetGuid() == pedroId ? (object)true : null;
        }, "Alice's contact linked");
        await EventuallyAsync(async () =>
        {
            var people = (await alice.GetJsonAsync("/api/summary")).GetProperty("counterparties").EnumerateArray().ToList();
            return people.Count == 1 && people[0].GetProperty("counterpartyId").GetGuid() == pedroId
                   && people[0].GetProperty("owedToMe").GetDecimal() == 80 ? (object)true : null;
        }, "a single balance with Pedro");
        var aliceHistory = (await alice.GetJsonAsync($"/api/transactions?contactId={pedroContact}&pageSize=50")).GetProperty("items");
        Assert.Equal(2, aliceHistory.GetArrayLength());

        // From now on they share it. Alice corrects the old loan: Pedro, now involved, confirms, and both books change.
        var loanId = loan.GetProperty("id").GetGuid();
        var change = await alice.PutAsJsonAsync($"/api/debts/operations/{loanId}",
            new { amount = 40, occurredAt = loanDate, reason = "It was 40" }, Json);
        var changeBody = await change.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.True(change.IsSuccessStatusCode, changeBody.ToString());
        Assert.Equal("Pending", changeBody.GetProperty("status").GetString());
        await Send(pedro, HttpMethod.Post, $"/api/debts/changes/{changeBody.GetProperty("changeId").GetGuid()}/accept");
        await EventuallyAsync(async () =>
            (await pedro.TotalsAsync("EUR")).GetProperty("iOwe").GetDecimal() == 70 ? (object)true : null, "Pedro's books corrected");
        await EventuallyAsync(async () =>
            (await alice.TotalsAsync("EUR")).GetProperty("owedToMe").GetDecimal() == 70 ? (object)true : null, "Alice's books corrected");

        // Pedro pays Alice back from his own wallet and both books reach zero.
        var pedroWallet = await pedro.CreateReadyWalletAsync("Pedro's bank", "EUR", 100);
        var aliceForPedro = await EventuallyAsync(async () =>
            (await pedro.GetJsonAsync("/api/contacts")).EnumerateArray()
                .Where(c => c.GetProperty("personId").GetGuid() == aliceId)
                .Select(c => (object?)c.GetProperty("id").GetGuid())
                .FirstOrDefault(), "Pedro's contact for Alice");
        var settlement = await pedro.PostJsonAsync("/api/debts/settlements",
            new { scope = "Contact", contactId = (Guid)aliceForPedro, currency = "EUR", walletId = pedroWallet }, HttpStatusCode.Accepted);
        Assert.Equal("Completed", (await pedro.CompletedOperationAsync(settlement)).GetProperty("status").GetString());

        await EventuallyAsync(async () => await pedro.WalletBalanceAsync(pedroWallet) == 30 ? (object)true : null, "Pedro paid");
        Assert.Equal(0, (await pedro.TotalsAsync("EUR")).GetProperty("iOwe").GetDecimal());
        await EventuallyAsync(async () =>
            (await alice.TotalsAsync("EUR")).GetProperty("owedToMe").GetDecimal() == 0 ? (object)true : null, "Alice was paid");
    }

    [Fact]
    public async Task A_rejected_request_changes_nothing()
    {
        var (alice, contactId, _) = await AliceWithLoanAsync(30);
        var (pedro, email, _) = await NewAccountAsync("pedro");

        var request = await RequestLinkAsync(alice, contactId, email);
        var rejected = await Send(pedro, HttpMethod.Post, $"/api/contacts/link-requests/{request}/reject");

        Assert.Equal("Rejected", rejected.GetProperty("status").GetString());
        var contact = (await alice.GetJsonAsync("/api/contacts")).EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == contactId);
        Assert.False(contact.GetProperty("isLinked").GetBoolean());
        Assert.Equal(JsonValueKind.Null, contact.GetProperty("pendingLinkRequestId").ValueKind);
        Assert.Equal(0, (await pedro.GetJsonAsync("/api/transactions")).GetProperty("totalCount").GetInt32());

        // Alice can ask again, and cancel it.
        var again = await RequestLinkAsync(alice, contactId, email);
        Assert.Equal("Cancelled", (await Send(alice, HttpMethod.Delete, $"/api/contacts/link-requests/{again}")).GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_closed_period_in_the_users_books_makes_it_fail()
    {
        var (alice, contactId, _) = await AliceWithLoanAsync(30, DateTimeOffset.UtcNow.AddDays(-10));
        var (pedro, email, _) = await NewAccountAsync("pedro");
        var locked = await pedro.PutAsJsonAsync("/api/settings/books-lock", new { lockedUntil = DateTimeOffset.UtcNow.AddDays(-5) }, Json);
        Assert.True(locked.IsSuccessStatusCode);

        var request = await RequestLinkAsync(alice, contactId, email);
        await Send(pedro, HttpMethod.Post, $"/api/contacts/link-requests/{request}/accept");

        var failed = await EventuallyAsync(async () =>
            (await alice.GetJsonAsync("/api/contacts/link-requests?includeClosed=true")).EnumerateArray()
                .Where(r => r.GetProperty("id").GetGuid() == request && r.GetProperty("status").GetString() == "Failed")
                .Select(r => (object?)r)
                .FirstOrDefault(), "the request to fail") as JsonElement?;
        Assert.Contains("closed", failed!.Value.GetProperty("failureReason").GetString());

        var contact = (await alice.GetJsonAsync("/api/contacts")).EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == contactId);
        Assert.False(contact.GetProperty("isLinked").GetBoolean());
        Assert.Equal(30, (await alice.TotalsAsync("EUR")).GetProperty("owedToMe").GetDecimal());
    }

    [Fact]
    public async Task A_second_contact_for_the_same_user_is_merged()
    {
        var (alice, oldContact, _) = await AliceWithLoanAsync(40);
        var (pedro, email, pedroId) = await NewAccountAsync("pedro");

        // Before this feature, the workaround was a new contact with the user's email (created once the
        // account is known to the Contacts module, which the request waits for).
        var request = await RequestLinkAsync(alice, oldContact, email);
        var newContact = (await alice.PostJsonAsync("/api/contacts", new { name = "Pedro (account)", email })).GetProperty("id").GetGuid();
        await AcceptAsync(pedro, alice, request);

        await EventuallyAsync(async () =>
        {
            var contacts = (await alice.GetJsonAsync("/api/contacts")).EnumerateArray().ToList();
            return contacts.Count == 1 && contacts[0].GetProperty("id").GetGuid() == newContact ? (object)true : null;
        }, "only the contact with the account left");
        await EventuallyAsync(async () =>
        {
            var people = (await alice.GetJsonAsync("/api/summary")).GetProperty("counterparties").EnumerateArray().ToList();
            return people.Count == 1 && people[0].GetProperty("counterpartyId").GetGuid() == pedroId
                   && people[0].GetProperty("owedToMe").GetDecimal() == 40 ? (object)true : null;
        }, "one balance with Pedro");
        Assert.Equal(1, (await alice.GetJsonAsync($"/api/transactions?contactId={newContact}")).GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task A_group_member_gets_the_group_and_their_position()
    {
        var alice = factory.ClientFor(NewUser("alice"));
        var aliceId = (await alice.GetJsonAsync("/api/me")).GetProperty("personId").GetGuid();
        var bank = await alice.CreateReadyWalletAsync("Bank", "EUR", 200);
        var contactId = (await alice.PostJsonAsync("/api/contacts", new { name = "Pedro" })).GetProperty("id").GetGuid();
        var oldPersonId = await PersonIdOfAsync(alice, contactId);
        var group = await alice.PostJsonAsync("/api/groups", new { name = "Trip", contactIds = new[] { contactId } });
        var groupId = group.GetProperty("id").GetGuid();

        var hotel = await EventuallyAsync(async () =>
        {
            var response = await alice.PostAsJsonAsync("/api/debts/splits", new
            {
                groupId, total = 100, currency = "EUR", method = "Equal", description = "Hotel", occurredAt = DateTimeOffset.UtcNow.AddDays(-2),
                payers = new[] { new { personId = aliceId, amount = 100, walletId = bank } },
                participants = new[] { new { personId = aliceId }, new { personId = oldPersonId } }
            }, Json);
            return response.StatusCode == HttpStatusCode.Accepted ? (object?)await response.Content.ReadFromJsonAsync<JsonElement>(Json) : null;
        }, "the group to reach Debts");
        await alice.CompletedOperationAsync((JsonElement)hotel);

        var (pedro, email, _) = await NewAccountAsync("pedro");
        var request = await RequestLinkAsync(alice, contactId, email);
        await AcceptAsync(pedro, alice, request);

        await EventuallyAsync(async () =>
            (await pedro.GetJsonAsync("/api/groups")).EnumerateArray().Any(g => g.GetProperty("id").GetGuid() == groupId) ? (object)true : null,
            "Pedro sees the group");
        await EventuallyAsync(async () =>
        {
            var people = (await pedro.GetJsonAsync("/api/summary")).GetProperty("counterparties").EnumerateArray().ToList();
            return people.Count == 1 && people[0].GetProperty("kind").GetString() == "Group"
                   && people[0].GetProperty("iOwe").GetDecimal() == 50 ? (object)true : null;
        }, "Pedro owes the group 50");
    }

    private async Task<(HttpClient Alice, Guid ContactId, Guid AliceId)> AliceWithLoanAsync(decimal amount, DateTimeOffset? when = null)
    {
        var alice = factory.ClientFor(NewUser("alice"));
        var aliceId = (await alice.GetJsonAsync("/api/me")).GetProperty("personId").GetGuid();
        var bank = await alice.CreateReadyWalletAsync("Bank", "EUR", 200);
        var contactId = (await alice.PostJsonAsync("/api/contacts", new { name = "Pedro" })).GetProperty("id").GetGuid();

        var loan = await alice.PostJsonAsync("/api/debts/loans",
            new { contactId, walletId = bank, amount, occurredAt = when ?? DateTimeOffset.UtcNow.AddDays(-1) }, HttpStatusCode.Accepted);
        await alice.CompletedOperationAsync(loan);

        return (alice, contactId, aliceId);
    }

    private async Task<(HttpClient Client, string Email, Guid UserId)> NewAccountAsync(string name)
    {
        var user = NewUser(name);
        var client = factory.ClientFor(user);
        var id = (await client.GetJsonAsync("/api/me")).GetProperty("personId").GetGuid();
        return (client, $"{user}@test.local", id);
    }

    /// <summary>Links a used contact: retries until the new user reached the Contacts module; returns the request id.</summary>
    private static async Task<Guid> RequestLinkAsync(HttpClient owner, Guid contactId, string email)
        => (Guid)(await EventuallyAsync(async () =>
        {
            var response = await owner.PostAsJsonAsync($"/api/contacts/{contactId}/link", new { email }, Json);
            if (!response.IsSuccessStatusCode)
                return null;

            var contact = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
            return (object?)contact.GetProperty("pendingLinkRequestId").GetGuid();
        }, "the link request"))!;

    private static async Task<JsonElement> PreviewAsync(HttpClient client, Guid requestId)
        => (JsonElement)(await EventuallyAsync(async () =>
        {
            var response = await client.GetAsync($"/api/debts/link-requests/{requestId}/preview");
            return response.IsSuccessStatusCode ? (object?)await response.Content.ReadFromJsonAsync<JsonElement>(Json) : null;
        }, "the preview"))!;

    private static async Task AcceptAsync(HttpClient user, HttpClient owner, Guid requestId)
    {
        var accepted = await Send(user, HttpMethod.Post, $"/api/contacts/link-requests/{requestId}/accept");
        Assert.Equal("Applying", accepted.GetProperty("status").GetString());

        await EventuallyAsync(async () =>
            (await owner.GetJsonAsync("/api/contacts/link-requests?includeClosed=true")).EnumerateArray()
                .Any(r => r.GetProperty("id").GetGuid() == requestId && r.GetProperty("status").GetString() == "Applied")
                ? (object)true
                : null, "the link to be applied");
    }

    private static async Task<Guid> PersonIdOfAsync(HttpClient client, Guid contactId)
        => (await client.GetJsonAsync("/api/contacts")).EnumerateArray()
            .Single(c => c.GetProperty("id").GetGuid() == contactId).GetProperty("personId").GetGuid();

    private static async Task<JsonElement> Send(HttpClient client, HttpMethod method, string path)
    {
        var response = await client.SendAsync(new HttpRequestMessage(method, path));
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"{method} {path}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");

        return await response.Content.ReadFromJsonAsync<JsonElement>(Json);
    }
}
