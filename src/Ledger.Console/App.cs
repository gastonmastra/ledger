using Ledger.Modules.Wallets.Application.CreateWallet;

namespace Ledger.Console;

public class App(
    CreateWalletHandler createWalletHandler)
{
    private readonly CreateWalletHandler _createWalletHandler = createWalletHandler;
    public async Task Run(string[] args)
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
        System.Console.WriteLine($"Result { result }");
    }

    static string ReadWalletName()
    {
        System.Console.Write("Ingrese nombre de la wallet: ");
        var userInput = System.Console.ReadLine();
        
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
        System.Console.Write("""
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

        var userChoice = System.Console.ReadLine();

        if (!int.TryParse(userChoice, out int choice))
            throw new ArgumentException("Debe ingresar un numero");

        if (choice < 1 | choice > 3)
            throw new ArgumentException("El numero debe estar entre 1 y 3");

        options.TryGetValue(choice, out string currency);

        return currency;
    }
}