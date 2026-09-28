using Ledger.Console.Menus;
using Ledger.Console.Menus.Transactions;

namespace Ledger.Console;

public class App(
    WalletsMenu walletsMenu,
    TransactionsMenu transactionsMenu,
    AccountsMenu accountsMenu)
{
    private readonly string _header = "LEDGER";

    private readonly AccountsMenu _accountsMenu = accountsMenu;
    private readonly TransactionsMenu _transactionsMenu = transactionsMenu;
    private readonly WalletsMenu _walletsMenu = walletsMenu;
    
    public async Task RunAsync(string[] args, CancellationToken cancellationToken)
    {
        bool executing = true;
        while (executing)
        {
            var option = ConsoleUi.ShowMenu(
                _header,
                new[]
                {
                    new Option(1, "Wallets"),
                    new Option(2, "Transactions"),
                    new Option(3, "Accounts"),
                    new Option(4, "Exit")
                });

            switch (option.Order)
            {
                case 1:
                    await _walletsMenu.RunAsync(cancellationToken);
                    break;
                case 2:
                    await _transactionsMenu.RunAsync(cancellationToken);
                    break;
                case 3:
                    await _accountsMenu.RunAsync(cancellationToken);
                    break;
                case 4:
                    executing = false;
                    break;
                default:
                    ConsoleUi.Header(_header);
                    ConsoleUi.ShowError("Not implemented");
                    return;
            }
        }
    }
}