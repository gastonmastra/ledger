using SystemConsole = System.Console;
using Ledger.Console.Menus;
using Ledger.Modules.Wallets.Application.CreateWallet;
using Ledger.Modules.Wallets.Application.GetWallets;

namespace Ledger.Console;

public class WalletsMenu(
    CreateWalletHandler createWalletHandler,
    GetWalletsHandler getWalletsHandler)
{
    private readonly string _header = "WALLETS";
    private readonly CreateWalletHandler _createWalletHandler = createWalletHandler;
    private readonly GetWalletsHandler _getWalletsHandler = getWalletsHandler;
    public async Task RunAsync()
    {
        var option = ConsoleUi.ShowMenu(
            _header,
            new[]
            {
                new Option(1, "List wallets"),
                new Option(2, "Create wallet"),
                new Option(3, "Back")
            });

        switch (option.Order)
        {
            case 1:
                await ListWallets();
                break;

            case 2:
                await CreateWallet();
                break;

            case 3:
                return;
        }
    }

    private async Task CreateWallet()
    {
        var userId = Guid.NewGuid();
        var walletName = ReadWalletName();
        var currency = ReadCurrency();

        var command = new CreateWalletCommand(
            userId,
            walletName,
            currency
        );
        var ct = new CancellationToken();
        var result = await _createWalletHandler.Handle(command, ct);
        SystemConsole.WriteLine($"Result { result }");
    }

    private async Task ListWallets()
    {
        SystemConsole.Clear();
        ConsoleUi.Header(_header);
        var userId = ConsoleUi.Read<Guid>("Enter user id: ");
        
        
        var ct = new CancellationToken();
        var query = new GetWalletsQuery(userId);
        var wallets = await _getWalletsHandler.Handle(query, ct);
        foreach (var wallet in wallets)
        {
            SystemConsole.WriteLine();
            SystemConsole.WriteLine($"Wallet name: {wallet.Name}");
            SystemConsole.WriteLine($"Wallet currency: {wallet.Currency}");
        }
        SystemConsole.Write("\nPress ENTER to continue...");
        SystemConsole.ReadLine();
    }

    private static string ReadWalletName()
    {
        SystemConsole.Write("Ingrese nombre de la wallet: ");
        var userInput = SystemConsole.ReadLine();
        
        if (string.IsNullOrWhiteSpace(userInput))
            throw new ArgumentException(
                "The wallet name should not be emtpy"
            );

        if (userInput.Length > 255)
            throw new ArgumentException(
                "The wallet name should not be bigger than 255 characters"
            );

        
        return userInput;
    }

    static string ReadCurrency()
    {
        SystemConsole.Write("""
        Select currency:
        1. Euro
        2. Dolar
        3. Argentine peso
        """);

        var options = new Dictionary<int, string>()
        {
            {1, "Eur"},
            {2, "Usd"},
            {3, "Ars"}  
        };

        var userChoice = SystemConsole.ReadLine();

        if (!int.TryParse(userChoice, out int choice))
            throw new ArgumentException("Debe ingresar un numero");

        if (choice < 1 | choice > 3)
            throw new ArgumentException("El numero debe estar entre 1 y 3");

        options.TryGetValue(choice, out string currency);

        return currency;
    }
}