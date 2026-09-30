using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ledger.Console.Api;

public sealed class ApiException(string message) : Exception(message);

/// <summary>Typed client of the Ledger HTTP API.</summary>
public sealed class LedgerApiClient(HttpClient http)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Waits until the API answers its health check (it may still be starting, e.g. when launched
    /// together with the console), so the first real call does not fail with "connection refused".
    /// </summary>
    public async Task WaitUntilAvailableAsync(CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        var announced = false;

        while (true)
        {
            string problem;

            try
            {
                using var response = await http.GetAsync("health", ct);

                if (response.IsSuccessStatusCode)
                    return;

                problem = $"{(int)response.StatusCode} {response.ReasonPhrase}";
            }
            catch (HttpRequestException ex)
            {
                problem = ex.Message;
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw new ApiException(
                    $"The Ledger API is not reachable at {http.BaseAddress} ({problem}). Start it first: " +
                    "VS Code 'API' or 'API + Console', or `dotnet run --project src/Ledger.Api --launch-profile http`.");
            }

            if (!announced)
            {
                System.Console.WriteLine($"Waiting for the Ledger API at {http.BaseAddress} ...");
                announced = true;
            }

            await Task.Delay(1000, ct);
        }
    }

    public Task<Me> MeAsync(CancellationToken ct) => GetAsync<Me>("api/me", ct);

    public Task<Summary> SummaryAsync(CancellationToken ct) => GetAsync<Summary>("api/summary", ct);

    // Wallets
    public Task<List<Wallet>> WalletsAsync(bool includeArchived, CancellationToken ct)
        => GetAsync<List<Wallet>>($"api/wallets?includeArchived={includeArchived}", ct);

    public Task<Wallet> CreateWalletAsync(
        string name, string currency, decimal openingBalance, DateTimeOffset? openingBalanceAt, CancellationToken ct)
        => SendAsync<Wallet>(HttpMethod.Post, "api/wallets", new { name, currency, openingBalance, openingBalanceAt }, ct);

    public Task<OpeningBalance> OpeningBalanceAsync(Guid walletId, CancellationToken ct)
        => GetAsync<OpeningBalance>($"api/transactions/opening-balance/{walletId}", ct);

    public Task ChangeOpeningBalanceAsync(Guid walletId, decimal amount, DateTimeOffset? occurredAt, CancellationToken ct)
        => SendAsync<object>(HttpMethod.Put, "api/transactions/opening-balance", new { walletId, amount, occurredAt }, ct);

    public Task AdjustBalanceAsync(
        Guid walletId, decimal actualBalance, DateTimeOffset? occurredAt, string? description, CancellationToken ct)
        => SendAsync<object>(HttpMethod.Post, "api/transactions/balance-adjustment",
            new { walletId, actualBalance, occurredAt, description }, ct);

    public Task RenameWalletAsync(Guid id, string name, CancellationToken ct)
        => SendAsync<object>(HttpMethod.Patch, $"api/wallets/{id}", new { name }, ct);

    public Task ArchiveWalletAsync(Guid id, CancellationToken ct)
        => SendAsync<object>(HttpMethod.Post, $"api/wallets/{id}/archive", null, ct);

    public Task DeleteWalletAsync(Guid id, CancellationToken ct)
        => SendAsync<object>(HttpMethod.Delete, $"api/wallets/{id}", null, ct);

    // Categories
    public Task<List<Category>> CategoriesAsync(CancellationToken ct) => GetAsync<List<Category>>("api/categories", ct);

    public Task<Category> CreateCategoryAsync(string name, string type, CancellationToken ct)
        => SendAsync<Category>(HttpMethod.Post, "api/categories", new { name, type }, ct);

    public Task RenameCategoryAsync(Guid id, string name, CancellationToken ct)
        => SendAsync<object>(HttpMethod.Patch, $"api/categories/{id}", new { name }, ct);

    public Task ArchiveCategoryAsync(Guid id, CancellationToken ct)
        => SendAsync<object>(HttpMethod.Post, $"api/categories/{id}/archive", null, ct);

    // Transactions
    public Task<PagedList<Transaction>> TransactionsAsync(TransactionFilter filter, CancellationToken ct)
    {
        var query = new List<string> { $"page={filter.Page}", "pageSize=10" };

        if (filter.WalletId is { } walletId) query.Add($"walletId={walletId}");
        if (filter.CategoryId is { } categoryId) query.Add($"categoryId={categoryId}");
        if (filter.ContactId is { } contactId) query.Add($"contactId={contactId}");
        if (filter.From is { } from) query.Add($"from={Uri.EscapeDataString(from.ToString("O"))}");
        if (filter.To is { } to) query.Add($"to={Uri.EscapeDataString(to.ToString("O"))}");

        return GetAsync<PagedList<Transaction>>($"api/transactions?{string.Join('&', query)}", ct);
    }

    public Task RecordIncomeAsync(
        Guid walletId, decimal amount, Guid? categoryId, string? description, DateTimeOffset? occurredAt, CancellationToken ct)
        => SendAsync<object>(HttpMethod.Post, "api/transactions/income",
            new { walletId, amount, categoryId, description, occurredAt }, ct);

    public Task RecordExpenseAsync(
        Guid walletId, decimal amount, Guid? categoryId, string? description, DateTimeOffset? occurredAt, CancellationToken ct)
        => SendAsync<object>(HttpMethod.Post, "api/transactions/expense",
            new { walletId, amount, categoryId, description, occurredAt }, ct);

    public Task TransferAsync(
        Guid fromWalletId, Guid toWalletId, decimal amount, decimal? receivedAmount, string? description,
        DateTimeOffset? occurredAt, CancellationToken ct)
        => SendAsync<object>(HttpMethod.Post, "api/transactions/transfer",
            new { fromWalletId, toWalletId, amount, receivedAmount, description, occurredAt }, ct);

    // Pending assignments
    public Task<List<PendingAssignment>> PendingAssignmentsAsync(CancellationToken ct)
        => GetAsync<List<PendingAssignment>>("api/pending-assignments", ct);

    public Task AssignWalletAsync(Guid assignmentId, Guid walletId, DateTimeOffset? occurredAt, CancellationToken ct)
        => SendAsync<object>(HttpMethod.Post, $"api/pending-assignments/{assignmentId}/assign", new { walletId, occurredAt }, ct);

    // Contacts
    public Task<List<Contact>> ContactsAsync(CancellationToken ct) => GetAsync<List<Contact>>("api/contacts", ct);

    public Task<Contact> CreateContactAsync(string name, string? email, CancellationToken ct)
        => SendAsync<Contact>(HttpMethod.Post, "api/contacts", new { name, email }, ct);

    public Task<Contact> LinkContactAsync(Guid id, string email, CancellationToken ct)
        => SendAsync<Contact>(HttpMethod.Post, $"api/contacts/{id}/link", new { email }, ct);

    // Groups
    public Task<List<Group>> GroupsAsync(CancellationToken ct) => GetAsync<List<Group>>("api/groups", ct);

    public Task<Group> CreateGroupAsync(string name, IReadOnlyList<Guid> contactIds, CancellationToken ct)
        => SendAsync<Group>(HttpMethod.Post, "api/groups", new { name, contactIds }, ct);

    public Task<Group> AddGroupMemberAsync(Guid groupId, Guid contactId, CancellationToken ct)
        => SendAsync<Group>(HttpMethod.Post, $"api/groups/{groupId}/members", new { contactId }, ct);

    public Task RemoveGroupMemberAsync(Guid groupId, Guid personId, CancellationToken ct)
        => SendAsync<object>(HttpMethod.Delete, $"api/groups/{groupId}/members/{personId}", null, ct);

    public Task<GroupBalances> GroupBalancesAsync(Guid groupId, CancellationToken ct)
        => GetAsync<GroupBalances>($"api/groups/{groupId}/balances", ct);

    // Debts
    public Task<MyDebts> DebtsAsync(bool includeSettled, CancellationToken ct)
        => GetAsync<MyDebts>($"api/debts?includeSettled={includeSettled}", ct);

    public Task<Operation> LendAsync(
        Guid contactId, Guid walletId, decimal amount, string? description, DateTimeOffset? occurredAt, CancellationToken ct)
        => SendAsync<Operation>(HttpMethod.Post, "api/debts/loans",
            new { contactId, walletId, amount, description, occurredAt }, ct);

    public Task<Operation> RecordInitialDebtAsync(
        Guid contactId, string direction, decimal amount, string currency, Guid? categoryId, string? description,
        DateTimeOffset? occurredAt, CancellationToken ct)
        => SendAsync<Operation>(HttpMethod.Post, "api/debts/initial",
            new { contactId, direction, amount, currency, categoryId, description, occurredAt }, ct);

    public Task<Operation> SplitAsync(
        Guid? groupId, decimal total, string currency, string method, IReadOnlyList<SplitPayer> payers,
        IReadOnlyList<SplitParticipant> participants, Guid? categoryId, string? description, DateTimeOffset? occurredAt,
        CancellationToken ct)
        => SendAsync<Operation>(HttpMethod.Post, "api/debts/splits",
            new { groupId, total, currency, method, payers, participants, categoryId, description, occurredAt }, ct);

    public Task<Operation> SettleAsync(object request, CancellationToken ct)
        => SendAsync<Operation>(HttpMethod.Post, "api/debts/settlements", request, ct);

    public Task<Operation> OperationAsync(Guid id, CancellationToken ct)
        => GetAsync<Operation>($"api/debts/operations/{id}", ct);

    /// <summary>Loans, splits and settlements are recorded asynchronously: wait for the outcome.</summary>
    public async Task<Operation> WaitForOperationAsync(Operation operation, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 40 && operation.Status == "Pending"; attempt++)
        {
            await Task.Delay(250, ct);
            operation = await OperationAsync(operation.Id, ct);
        }

        return operation;
    }

    public Task<Report> ReportAsync(DateTimeOffset from, DateTimeOffset to, string? currency, CancellationToken ct)
    {
        var query = $"from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}" +
                    $"&timeZone={Uri.EscapeDataString(TimeZoneInfo.Local.Id)}";

        if (currency is not null) query += $"&currency={currency}";

        return GetAsync<Report>($"api/reports?{query}", ct);
    }

    private Task<T> GetAsync<T>(string path, CancellationToken ct) => SendAsync<T>(HttpMethod.Get, path, null, ct);

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        // 503 means another module is still catching up (eventual consistency): retry a few times.
        for (var attempt = 1; ; attempt++)
        {
            using var request = new HttpRequestMessage(method, path);

            if (body is not null)
                request.Content = JsonContent.Create(body, options: Json);

            using var response = await http.SendAsync(request, ct);

            if (response.StatusCode == HttpStatusCode.ServiceUnavailable && attempt < 10)
            {
                await Task.Delay(500, ct);
                continue;
            }

            if (!response.IsSuccessStatusCode)
                throw new ApiException(await ReadProblemAsync(response, ct));

            if (typeof(T) == typeof(object) || response.Content.Headers.ContentLength == 0)
                return default!;

            return (await response.Content.ReadFromJsonAsync<T>(Json, ct))!;
        }
    }

    private static async Task<string> ReadProblemAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Json, ct);

            if (problem.TryGetProperty("errors", out var errors))
                return string.Join(" ", errors.EnumerateObject().SelectMany(e => e.Value.EnumerateArray().Select(v => v.GetString())));

            if (problem.TryGetProperty("detail", out var detail) && detail.GetString() is { Length: > 0 } text)
                return text;

            if (problem.TryGetProperty("title", out var title))
                return title.GetString() ?? response.ReasonPhrase ?? "Request failed";
        }
        catch (JsonException)
        {
        }

        return $"{(int)response.StatusCode} {response.ReasonPhrase}";
    }
}
