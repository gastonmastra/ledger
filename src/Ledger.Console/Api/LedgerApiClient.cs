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

    public Task ChangeOpeningBalanceAsync(Guid walletId, decimal amount, DateTimeOffset? occurredAt, string reason, CancellationToken ct)
        => SendAsync<object>(HttpMethod.Put, "api/transactions/opening-balance", new { walletId, amount, occurredAt, reason }, ct);

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
        if (filter.IncludeHistory) query.Add("includeHistory=true");

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

    /// <summary>Records a corrected version (the current one is reversed). For a transfer, walletId is the source.</summary>
    public Task ReviseTransactionAsync(
        Guid id, Guid walletId, Guid? toWalletId, decimal amount, decimal? receivedAmount, Guid? categoryId,
        string? description, DateTimeOffset occurredAt, string reason, CancellationToken ct)
        => SendAsync<object>(HttpMethod.Put, $"api/transactions/{id}",
            new { walletId, toWalletId, amount, receivedAmount, categoryId, description, occurredAt, reason }, ct);

    public Task DeleteTransactionAsync(Guid id, string reason, CancellationToken ct)
        => SendAsync<object>(HttpMethod.Delete, $"api/transactions/{id}", new { reason }, ct);

    public Task RestoreTransactionAsync(Guid id, string reason, CancellationToken ct)
        => SendAsync<object>(HttpMethod.Post, $"api/transactions/{id}/restore", new { reason }, ct);

    public Task<List<TransactionHistoryItem>> TransactionHistoryAsync(Guid id, CancellationToken ct)
        => GetAsync<List<TransactionHistoryItem>>($"api/transactions/{id}/history", ct);

    public Task<PagedList<TransactionActivity>> TransactionActivityAsync(int pageSize, CancellationToken ct)
        => GetAsync<PagedList<TransactionActivity>>($"api/transactions/activity?pageSize={pageSize}", ct);

    // Settings
    public Task<BooksLock> BooksLockAsync(CancellationToken ct) => GetAsync<BooksLock>("api/settings/books-lock", ct);

    public Task<BooksLock> SetBooksLockAsync(DateTimeOffset? lockedUntil, CancellationToken ct)
        => SendAsync<BooksLock>(HttpMethod.Put, "api/settings/books-lock", new { lockedUntil }, ct);

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

    public Task<List<LinkRequest>> LinkRequestsAsync(bool includeClosed, CancellationToken ct)
        => GetAsync<List<LinkRequest>>($"api/contacts/link-requests?includeClosed={includeClosed}", ct);

    public Task<LinkRequest> AcceptLinkRequestAsync(Guid id, CancellationToken ct)
        => SendAsync<LinkRequest>(HttpMethod.Post, $"api/contacts/link-requests/{id}/accept", null, ct);

    public Task<LinkRequest> RejectLinkRequestAsync(Guid id, CancellationToken ct)
        => SendAsync<LinkRequest>(HttpMethod.Post, $"api/contacts/link-requests/{id}/reject", null, ct);

    public Task<LinkRequest> CancelLinkRequestAsync(Guid id, CancellationToken ct)
        => SendAsync<LinkRequest>(HttpMethod.Delete, $"api/contacts/link-requests/{id}", null, ct);

    public Task<LinkPreview> LinkPreviewAsync(Guid id, CancellationToken ct)
        => GetAsync<LinkPreview>($"api/debts/link-requests/{id}/preview", ct);

    /// <summary>Accepting starts a merge in the background: wait until it is applied or fails.</summary>
    public async Task<LinkRequest?> WaitForLinkAsync(Guid id, CancellationToken ct)
    {
        LinkRequest? request = null;

        for (var attempt = 0; attempt < 40 && request?.Status is null or "Applying"; attempt++)
        {
            await Task.Delay(250, ct);
            request = (await LinkRequestsAsync(includeClosed: true, ct)).FirstOrDefault(r => r.Id == id);
        }

        return request;
    }

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
        Guid contactId, string direction, decimal amount, string currency, string? description,
        DateTimeOffset? occurredAt, CancellationToken ct)
        => SendAsync<Operation>(HttpMethod.Post, "api/debts/initial",
            new { contactId, direction, amount, currency, description, occurredAt }, ct);

    public Task<Operation> SplitAsync(
        Guid? groupId, decimal total, string currency, string method, IReadOnlyList<SplitPayer> payers,
        IReadOnlyList<SplitParticipant> participants, Guid? categoryId, string? description, DateTimeOffset? occurredAt,
        CancellationToken ct)
        => SendAsync<Operation>(HttpMethod.Post, "api/debts/splits",
            new { groupId, total, currency, method, payers, participants, categoryId, description, occurredAt }, ct);

    /// <summary>Pays one of a group's suggested payments.</summary>
    public Task<Operation> SettleAsync(object request, CancellationToken ct)
        => SendAsync<Operation>(HttpMethod.Post, "api/debts/settlements", request, ct);

    /// <summary>Money paid between me and a contact (outside groups): it pays our debts oldest first.</summary>
    public Task<Operation> RecordPaymentAsync(
        Guid contactId, string currency, string direction, decimal amount, Guid walletId, string? description,
        DateTimeOffset? occurredAt, CancellationToken ct)
        => SendAsync<Operation>(HttpMethod.Post, "api/debts/payments",
            new { contactId, currency, direction, amount, walletId, description, occurredAt }, ct);

    /// <summary>What a new payment (contact, currency, direction) or an edit of one (operationId) would pay.</summary>
    public Task<PaymentPreview> PreviewPaymentAsync(object request, CancellationToken ct)
        => SendAsync<PaymentPreview>(HttpMethod.Post, "api/debts/payments/preview", request, ct);

    public Task<IReadOnlyList<DebtPayment>> DebtPaymentsAsync(Guid debtId, CancellationToken ct)
        => GetAsync<IReadOnlyList<DebtPayment>>($"api/debts/{debtId}/payments", ct);

    public Task<Operation> OperationAsync(Guid id, CancellationToken ct)
        => GetAsync<Operation>($"api/debts/operations/{id}", ct);

    public Task<PagedList<OperationDetails>> OperationsAsync(Guid? groupId, bool includeHistory, int page, CancellationToken ct)
    {
        var query = $"page={page}&pageSize=10&includeHistory={includeHistory}";
        if (groupId is { } id) query += $"&groupId={id}";

        return GetAsync<PagedList<OperationDetails>>($"api/debts/operations?{query}", ct);
    }

    public Task<OperationDetails> OperationDetailsAsync(Guid id, CancellationToken ct)
        => GetAsync<OperationDetails>($"api/debts/operations/{id}", ct);

    public Task<OperationHistory> OperationHistoryAsync(Guid id, CancellationToken ct)
        => GetAsync<OperationHistory>($"api/debts/operations/{id}/history", ct);

    /// <summary>New values of an operation (every field of its kind) and why. Linked users confirm it first.</summary>
    public Task<ChangeResult> ReviseOperationAsync(Guid id, object request, CancellationToken ct)
        => SendAsync<ChangeResult>(HttpMethod.Put, $"api/debts/operations/{id}", request, ct);

    public Task<ChangeResult> DeleteOperationAsync(Guid id, string reason, CancellationToken ct)
        => SendAsync<ChangeResult>(HttpMethod.Delete, $"api/debts/operations/{id}", new { reason }, ct);

    public Task<ChangeResult> RestoreOperationAsync(Guid id, string reason, CancellationToken ct)
        => SendAsync<ChangeResult>(HttpMethod.Post, $"api/debts/operations/{id}/restore", new { reason }, ct);

    /// <param name="all">False: only the changes waiting for my answer.</param>
    public Task<List<OperationChange>> ChangesAsync(bool all, CancellationToken ct)
        => GetAsync<List<OperationChange>>($"api/debts/changes?filter={(all ? "All" : "ToReview")}", ct);

    public Task<ChangeResult> RespondToChangeAsync(Guid id, bool accept, string? reason, CancellationToken ct)
        => SendAsync<ChangeResult>(HttpMethod.Post, $"api/debts/changes/{id}/{(accept ? "accept" : "reject")}", new { reason }, ct);

    public Task<PagedList<OperationChange>> DebtActivityAsync(int pageSize, CancellationToken ct)
        => GetAsync<PagedList<OperationChange>>($"api/debts/activity?pageSize={pageSize}", ct);

    /// <summary>A change being applied updates the books asynchronously: wait for the outcome.</summary>
    public async Task<(string Status, string? Failure)> WaitForChangeAsync(ChangeResult result, CancellationToken ct)
    {
        var status = result.Status;
        string? failure = null;

        for (var attempt = 0; attempt < 40 && status == "Applying"; attempt++)
        {
            await Task.Delay(250, ct);
            var change = (await OperationHistoryAsync(result.OperationId, ct)).Changes.FirstOrDefault(c => c.Id == result.ChangeId);
            status = change?.Status ?? status;
            failure = change?.FailureReason;
        }

        return (status, failure);
    }

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

    // Investments
    public Task<List<Asset>> AssetsAsync(bool includeArchived, CancellationToken ct)
        => GetAsync<List<Asset>>($"api/investments/assets?includeArchived={includeArchived}", ct);

    public Task<Asset> CreateAssetAsync(string name, string? symbol, string type, string currency, CancellationToken ct)
        => SendAsync<Asset>(HttpMethod.Post, "api/investments/assets", new { name, symbol, type, currency }, ct);

    public Task UpdateAssetAsync(Guid id, string name, string? symbol, CancellationToken ct)
        => SendAsync<object>(HttpMethod.Put, $"api/investments/assets/{id}", new { name, symbol }, ct);

    public Task ArchiveAssetAsync(Guid id, CancellationToken ct)
        => SendAsync<object>(HttpMethod.Post, $"api/investments/assets/{id}/archive", null, ct);

    public Task<List<AssetPrice>> AssetPricesAsync(Guid id, CancellationToken ct)
        => GetAsync<List<AssetPrice>>($"api/investments/assets/{id}/prices", ct);

    public Task RecordPriceAsync(Guid id, decimal? buyPrice, decimal? sellPrice, DateTimeOffset occurredAt, CancellationToken ct)
        => SendAsync<object>(HttpMethod.Post, $"api/investments/assets/{id}/prices", new { buyPrice, sellPrice, occurredAt }, ct);

    /// <param name="kind">"buy", "sell" or "opening" (a holding already owned).</param>
    /// <param name="counterPrice">Buy/opening: the current sell price. Sell: the current buy price.</param>
    public Task<Trade> RecordTradeAsync(
        string kind, Guid assetId, decimal quantity, decimal unitPrice, Guid? walletId, decimal? walletAmount,
        decimal? counterPrice, DateTimeOffset occurredAt, string? description, CancellationToken ct)
        => SendAsync<Trade>(HttpMethod.Post, $"api/investments/trades/{kind}", new
        {
            assetId,
            quantity,
            unitPrice,
            walletId,
            walletAmount,
            sellPrice = kind == "sell" ? null : counterPrice,
            buyPrice = kind == "sell" ? counterPrice : null,
            occurredAt,
            description
        }, ct);

    public Task<PagedList<Trade>> TradesAsync(Guid? assetId, int page, CancellationToken ct)
        => GetAsync<PagedList<Trade>>($"api/investments/trades?page={page}&pageSize=20{(assetId is { } id ? $"&assetId={id}" : "")}", ct);

    /// <summary>Trades are recorded asynchronously: wait for the outcome.</summary>
    public async Task<Trade> WaitForTradeAsync(Trade trade, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 40 && trade.Status == "Pending"; attempt++)
        {
            await Task.Delay(250, ct);
            trade = await GetAsync<Trade>($"api/investments/trades/{trade.Id}", ct);
        }

        return trade;
    }

    public Task<List<InvestmentsTotal>> InvestmentsSummaryAsync(CancellationToken ct)
        => GetAsync<List<InvestmentsTotal>>("api/investments/summary", ct);

    public Task<InvestmentsReport> InvestmentsReportAsync(DateTimeOffset from, DateTimeOffset to, string currency, CancellationToken ct)
        => GetAsync<InvestmentsReport>(
            $"api/investments/report?from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}" +
            $"&timeZone={Uri.EscapeDataString(TimeZoneInfo.Local.Id)}&currency={currency}", ct);

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
