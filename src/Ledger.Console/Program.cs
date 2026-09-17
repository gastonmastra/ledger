using Ledger.Modules.Wallets.Application.CreateWallet;

Console.WriteLine("Welcome to ledger!");

var userId = Guid.NewGuid();
var walletName = ReadWalletName();
var currency = ReadCurrency();

var command = new CreateWalletCommand(
    userId,
    walletName,
    currency
);

static string ReadWalletName()
{
    Console.Write("Ingrese nombre de la wallet: ");
    var userInput = Console.ReadLine();
    
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
    Console.Write("""
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

    var userChoice = Console.ReadLine();

    if (!int.TryParse(userChoice, out int choice))
        throw new ArgumentException("Debe ingresar un numero");

    if (choice < 1 | choice > 3)
        throw new ArgumentException("El numero debe estar entre 1 y 3");

    options.TryGetValue(choice, out string currency);

    return currency;
}