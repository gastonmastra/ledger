using Ledger.Modules.Transactions.Application.CreateTransaction;
using Ledger.Modules.Transactions.Application.GetTransaction;
using Microsoft.Extensions.Logging;

namespace Ledger.Console.Menus.Transactions;

public class TransactionsMenu(
    CreateTransactionHandler createTransactionHandler,
    GetTransactionHandler getTransactionHandler,
    ILogger<TransactionsMenu> logger)
{   
    private readonly string _header = "Transactions";
    private readonly CreateTransactionHandler _createTransactionHandler = createTransactionHandler;
    private readonly GetTransactionHandler _getTransactionHandler = getTransactionHandler;
    private readonly ILogger<TransactionsMenu> _logger = logger;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var option = ConsoleUi.ShowMenu(
            "Wallets",
            new[]
            {
                new Option(1, "List transactions"),
                new Option(2, "Create transaction"),
                new Option(3, "Back")
            });

        while (option.Order != 3)
        {
            switch (option.Order)
            {
                case 1:
                    await ListTransactionsAsync(cancellationToken);
                    break;
                default:
                    ConsoleUi.Header(_header);
                    ConsoleUi.ShowError("Not implemented");
                    return;
            }
        }
    }

    private async Task ListTransactionsAsync(CancellationToken cancellationToken)
    {
        ConsoleUi.Header(_header);
        var transactionId = ConsoleUi
            .Read<Guid>("Enter transaction id: ");
        var getTransactionQuery = new GetTransactionQuery(transactionId);
        var transaction = await _getTransactionHandler
            .Handle(getTransactionQuery, cancellationToken);

        if (transaction is null)
        {
            ConsoleUi.ShowInfo("Not found");
            return;
        }

        ConsoleUi.ShowInfo(
            "Transaction",
            ("Description", transaction.Description),
            ("Occurred at", transaction.OccurredAt.ToString())
        );
        ConsoleUi.ShowTable(
            "Postings",
            transaction.Postings,
            ("Account", posting => posting.AccountId),
            ("Amoung", posting => posting.Amount)
        );

        ConsoleUi.Pause();
    }
}