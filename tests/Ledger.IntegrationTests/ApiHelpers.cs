using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ledger.IntegrationTests;

public static class ApiHelpers
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task<JsonElement> GetJsonAsync(this HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        await EnsureAsync(response, HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Json);
    }

    public static async Task<JsonElement> PostJsonAsync(
        this HttpClient client,
        string path,
        object body,
        HttpStatusCode expected = HttpStatusCode.Created)
    {
        // Something created a moment ago may still be propagating to other modules (404/503): retry briefly.
        for (var attempt = 1; ; attempt++)
        {
            var response = await client.PostAsJsonAsync(path, body, Json);

            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.ServiceUnavailable
                && response.StatusCode != expected
                && attempt < 30)
            {
                await Task.Delay(300);
                continue;
            }

            await EnsureAsync(response, expected);

            return response.Content.Headers.ContentLength == 0
                ? default
                : await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        }
    }

    public static async Task<T> EventuallyAsync<T>(Func<Task<T?>> probe, string what, int seconds = 30)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);

        while (DateTime.UtcNow < deadline)
        {
            if (await probe() is { } value)
                return value;

            await Task.Delay(250);
        }

        throw new TimeoutException($"Timed out waiting for {what}.");
    }

    public static async Task<Guid> CreateReadyWalletAsync(this HttpClient client, string name, string currency, decimal opening)
    {
        var wallet = await client.PostJsonAsync("/api/wallets", new { name, currency, openingBalance = opening });
        var id = wallet.GetProperty("id").GetGuid();

        await EventuallyAsync(async () =>
        {
            var current = await client.GetJsonAsync($"/api/wallets/{id}");
            return current.GetProperty("isReady").GetBoolean() && current.GetProperty("balance").GetDecimal() == opening
                ? (object)true
                : null;
        }, $"wallet {name} to be ready");

        return id;
    }

    public static async Task<decimal> WalletBalanceAsync(this HttpClient client, Guid walletId)
        => (await client.GetJsonAsync($"/api/wallets/{walletId}")).GetProperty("balance").GetDecimal();

    public static async Task<JsonElement> CompletedOperationAsync(this HttpClient client, JsonElement operation)
    {
        var id = operation.GetProperty("id").GetGuid();

        return await EventuallyAsync<JsonElement?>(async () =>
        {
            var current = await client.GetJsonAsync($"/api/debts/operations/{id}");
            return current.GetProperty("status").GetString() == "Pending" ? null : current;
        }, $"operation {id}") ?? default;
    }

    public static async Task<JsonElement> TotalsAsync(this HttpClient client, string currency)
    {
        var summary = await client.GetJsonAsync("/api/summary");
        return summary.GetProperty("totals").EnumerateArray().Single(t => t.GetProperty("currency").GetString() == currency);
    }

    private static async Task EnsureAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        if (response.StatusCode != expected)
        {
            throw new InvalidOperationException(
                $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri} returned {(int)response.StatusCode}, " +
                $"expected {(int)expected}: {await response.Content.ReadAsStringAsync()}");
        }
    }
}
