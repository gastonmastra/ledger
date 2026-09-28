using Ledger.Console;
using Ledger.Console.Menus;
using Ledger.Console.Menus.Transactions;
using Ledger.Modules.Accounts;
using Ledger.Modules.Transactions;
using Ledger.Modules.Wallets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using IHost host = CreateHostBuilder(args).Build();
using var scope = host.Services.CreateScope();

var services = scope.ServiceProvider;

try
{
    using var cancellationTokenSource = new CancellationTokenSource();

    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cancellationTokenSource.Cancel();
    };
    var app = services.GetRequiredService<App>();
    await app.RunAsync(args, cancellationTokenSource.Token);
}
catch (Exception ex)
{
    Console.WriteLine(ex.Message);
}

static IHostBuilder CreateHostBuilder(string[] args)
{
    return Host.CreateDefaultBuilder(args)
        .ConfigureServices((builder, services) =>
        {
            services.AddScoped<App>();
            services.AddScoped<WalletsMenu>();
            services.AddScoped<TransactionsMenu>();
            services.AddScoped<AccountsMenu>();
            services.AddWalletsModule(builder.Configuration);
            services.AddTransactionsModule(builder.Configuration);
            services.AddAccountsModule(builder.Configuration);
        });
}