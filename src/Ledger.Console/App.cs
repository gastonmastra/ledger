using Ledger.Console.Api;
using Ledger.Console.Menus;

namespace Ledger.Console;

public sealed class App(
    LedgerApiClient api,
    SummaryScreen summary,
    WalletsMenu wallets,
    TransactionsMenu transactions,
    CategoriesMenu categories,
    ContactsMenu contacts,
    GroupsMenu groups,
    DebtsMenu debts,
    PendingMenu pending)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await api.WaitUntilAvailableAsync(cancellationToken);

        var me = await api.MeAsync(cancellationToken);

        while (!cancellationToken.IsCancellationRequested)
        {
            var pendingCount = (await api.PendingAssignmentsAsync(cancellationToken)).Count;

            var option = ConsoleUi.ShowMenu(
                "LEDGER",
                [
                    new Option(1, "My money (summary)"),
                    new Option(2, "Wallets"),
                    new Option(3, "Transactions"),
                    new Option(4, "Categories"),
                    new Option(5, "Contacts"),
                    new Option(6, "Groups"),
                    new Option(7, "Debts & splits"),
                    new Option(8, pendingCount == 0 ? "Pending wallet assignments" : $"Pending wallet assignments ({pendingCount})"),
                    new Option(9, "Exit")
                ],
                subtitle: $"Logged in as {me.DisplayName} ({me.Email})");

            try
            {
                switch (option.Order)
                {
                    case 1: await summary.ShowAsync(cancellationToken); break;
                    case 2: await wallets.RunAsync(cancellationToken); break;
                    case 3: await transactions.RunAsync(cancellationToken); break;
                    case 4: await categories.RunAsync(cancellationToken); break;
                    case 5: await contacts.RunAsync(cancellationToken); break;
                    case 6: await groups.RunAsync(cancellationToken); break;
                    case 7: await debts.RunAsync(cancellationToken); break;
                    case 8: await pending.RunAsync(cancellationToken); break;
                    case 9: return;
                }
            }
            catch (ApiException ex)
            {
                ConsoleUi.Error(ex.Message);
                ConsoleUi.Pause();
            }
        }
    }
}
