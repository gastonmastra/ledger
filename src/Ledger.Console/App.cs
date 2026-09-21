using SystemConsole = System.Console;

namespace Ledger.Console;

public class App(
    WalletsMenu walletsMenu)
{
    private readonly WalletsMenu _walletsMenu = walletsMenu;
    public async Task Run(string[] args)
    {
        bool executing = true;
        while (executing)
        {
            ShowMainOptions();

            var option = SystemConsole.ReadLine();

            switch (option)
            {
                case "1":
                    await _walletsMenu.RunAsync();
                    break;
                case "2":
                    SystemConsole.Clear();
                    SystemConsole.WriteLine("======================================");
                    SystemConsole.WriteLine("             Wallets module.          ");
                    SystemConsole.WriteLine("======================================");
                    SystemConsole.WriteLine("Not implemented.");
                    SystemConsole.WriteLine("Press ENTER...");
                    SystemConsole.ReadLine();
                    break;
                case "3":
                    executing = false;
                    break;
                default: 
                    ShowErrorMessage("Invalid option.");
                    break;

            }
        }
    }

    private static void ShowErrorMessage(string error)
    {
        SystemConsole.ForegroundColor = ConsoleColor.Red;
        SystemConsole.WriteLine($"\n[ERROR] {error}");
        SystemConsole.ResetColor();
        SystemConsole.WriteLine("Press ENTER to continue...");
        SystemConsole.ReadLine();
    }

    private static void ShowMainOptions()
    {
        SystemConsole.Clear();
        SystemConsole.WriteLine("======================================");
        SystemConsole.WriteLine("               LEDGER                 ");
        SystemConsole.WriteLine("======================================");
        SystemConsole.WriteLine("1. Wallets module");
        SystemConsole.WriteLine("2. Transactions module");
        SystemConsole.WriteLine("3. Exit");
        SystemConsole.Write("\nChoose an option: ");
    }
}