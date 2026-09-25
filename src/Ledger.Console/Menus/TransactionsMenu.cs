using Ledger.Modules.Transactions.Application.CreateTransaction;
using Microsoft.Extensions.Logging;

namespace Ledger.Console.Menus.Transactions;

public class TransactionsMenu(
    CreateTransactionHandler createTransactionHandler,
    ILogger<TransactionsMenu> logger)
{   
    private readonly string _header = "Transactions";
    private readonly CreateTransactionHandler _createTransactionHandler = createTransactionHandler;
    private readonly ILogger<TransactionsMenu> _logger = logger;

    public async Task RunAsync()
    {
        var option = ConsoleUi.ShowMenu(
            "Wallets",
            new[]
            {
                new Option(1, "List transactions"),
                new Option(2, "Create transaction"),
                new Option(3, "Back")
            });

        switch (option.Order)
        {
            default:
                ConsoleUi.Header(_header);
                ConsoleUi.ShowError("Not implemented");
                return;
        }
    }
}