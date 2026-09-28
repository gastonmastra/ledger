using System.Security.Cryptography.X509Certificates;
using Ledger.Modules.Accounts.Application.CreateAccount;
using Ledger.Modules.Accounts.Application.GetAccount;
using Ledger.Modules.Accounts.Domain;
using Ledger.Modules.Transactions.Domain;

namespace Ledger.Console.Menus;

public sealed class AccountsMenu(
    CreateAccountHandler createAccountHandler,
    GetAccountHandler getAccountHandler)
{
    private readonly string _header = "Accounts";
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var executing = true;
        while (executing)
        {
            System.Console.Clear();

            var option = ConsoleUi.ShowMenu(
                _header,
                [
                    new Option(1, "List accounts"),
                    new Option(2, "Create account"),
                    new Option(3, "Get account"),
                    new Option(4, "Archive account"),
                    new Option(5, "Back")
                ]);

            switch (option.Order)
            {
                case 1:
                    await ListAccountsAsync(cancellationToken);
                    break;

                case 2:
                    await CreateAccountAsync(cancellationToken);
                    break;

                case 3:
                    await GetAccountAsync(cancellationToken);
                    break;

                case 4:
                    await ArchiveAccountAsync(cancellationToken);
                    return;

                case 5:
                    executing = false;
                    break;

                default:
                    ConsoleUi.ShowError("Invalid option.");
                    break;
            }
        }
    }

    private async Task GetAccountAsync(CancellationToken cancellationToken)
    {
        ConsoleUi.Header(_header);

        var id = ConsoleUi.Read<Guid>("Enter account id: ");

        var query = new GetAccountQuery(id);

        var account = await getAccountHandler.HandleAsync(query, cancellationToken);

        ConsoleUi.ShowInfo(
            "Account found", 
            ("Id", account.Id),
            ("Name", account.Name),
            ("Type", account.Type.ToString()),
            ("Is archived", account.IsArchived.ToString())
        );

        ConsoleUi.Pause();
    }

    private async Task CreateAccountAsync(
        CancellationToken cancellationToken)
    {
        ConsoleUi.Header("Create Account");

        var name = ConsoleUi.Read<string>("Name: ");

        System.Console.WriteLine("Account type:");
        System.Console.WriteLine("1. Asset");
        System.Console.WriteLine("2. Liability");
        System.Console.WriteLine("3. Equity");
        System.Console.WriteLine("4. Income");
        System.Console.WriteLine("5. Expense");
        System.Console.WriteLine();

        var typeOption = ConsoleUi.Read<int>("Type: ");

        if (!TryParseAccountType(typeOption, out var type))
        {
            ConsoleUi.ShowError("Invalid account type.");
            return;
        }

        var command = new CreateAccountCommand(
            name,
            type);

        var accountId = await createAccountHandler.Handle(
            command,
            cancellationToken);

        ConsoleUi.ShowInfo(
            "Account created",
            ("Name", name),
            ("Type", type.ToString()),
            ("Id", accountId.ToString()));

        ConsoleUi.Pause();
    }

    private static bool TryParseAccountType(
        int option,
        out AccountType type)
    {
        type = option switch
        {
            1 => AccountType.Asset,
            2 => AccountType.Liability,
            3 => AccountType.Equity,
            4 => AccountType.Income,
            5 => AccountType.Expense,
            _ => default
        };

        return option is >= 1 and <= 5;
    }

    private async Task ListAccountsAsync(
        CancellationToken cancellationToken)
    {
        // TODO: implement GetAccountsHandler
        await Task.CompletedTask;
    }

    private async Task ArchiveAccountAsync(
        CancellationToken cancellationToken)
    {
        // TODO: implement ArchiveAccountHandler
        await Task.CompletedTask;
    }
}