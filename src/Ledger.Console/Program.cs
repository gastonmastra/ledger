using Ledger.Console;
using Ledger.Console.Api;
using Ledger.Console.Auth;
using Ledger.Console.Menus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

builder.Services.AddSingleton<KeycloakLogin>();

// Every API call carries the Keycloak access token (refreshed automatically).
builder.Services
    .AddHttpClient<LedgerApiClient>(client =>
        client.BaseAddress = new Uri(builder.Configuration["Api:BaseUrl"]!.TrimEnd('/') + "/"))
    .ConfigurePrimaryHttpMessageHandler(sp =>
        sp.GetRequiredService<KeycloakLogin>().AuthenticatedHandler
        ?? throw new InvalidOperationException("Log in before calling the API."))
    // The handler holds the session tokens: keep it for the whole run.
    .SetHandlerLifetime(Timeout.InfiniteTimeSpan);

builder.Services
    .AddTransient<App>()
    .AddTransient<SplitWizard>()
    .AddTransient<SummaryScreen>()
    .AddTransient<WalletsMenu>()
    .AddTransient<TransactionsMenu>()
    .AddTransient<CategoriesMenu>()
    .AddTransient<ContactsMenu>()
    .AddTransient<GroupsMenu>()
    .AddTransient<DebtsMenu>()
    .AddTransient<PendingMenu>()
    .AddTransient<ReportsMenu>()
    .AddTransient<OperationsScreen>()
    .AddTransient<ChangesScreen>()
    .AddTransient<ActivityScreen>()
    .AddTransient<SettingsMenu>();

using var host = builder.Build();
using var cancellation = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};

try
{
    ConsoleUi.Header("LEDGER");
    await host.Services.GetRequiredService<KeycloakLogin>().LoginAsync(cancellation.Token);
    await host.Services.GetRequiredService<App>().RunAsync(cancellation.Token);
    return 0;
}
catch (OperationCanceledException)
{
    return 0;
}
catch (Exception ex)
{
    ConsoleUi.Error(ex.Message);
    return 1;
}
