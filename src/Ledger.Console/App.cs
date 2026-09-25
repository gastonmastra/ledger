using Ledger.Console.Menus;
using Ledger.Console.Menus.Transactions;

namespace Ledger.Console;

public class App(
    WalletsMenu walletsMenu,
    TransactionsMenu transactionsMenu)
{
    private readonly string _header = "LEDGER";
    private readonly WalletsMenu _walletsMenu = walletsMenu;
    private readonly TransactionsMenu _transactionsMenu = transactionsMenu;
    public async Task Run(string[] args)
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
                    new Option(3, "Exit")
                });

            switch (option.Order)
            {
                case 1:
                    await _walletsMenu.RunAsync();
                    break;
                case 2:
                    await _transactionsMenu.RunAsync();
                    break;
                case 3:
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