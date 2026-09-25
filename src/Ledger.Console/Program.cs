using Ledger.Console;
using Ledger.Console.Menus.Transactions;
using Ledger.Modules.Transactions;
using Ledger.Modules.Wallets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using IHost host = CreateHostBuilder(args).Build();
using var scope = host.Services.CreateScope();

var services = scope.ServiceProvider;

try
{
    await services.GetRequiredService<App>().Run(args);
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
            services.AddWalletsModule(builder.Configuration);
            services.AddTransactionsModule(builder.Configuration);
        });
}