using Ledger.Console.Api;
using SystemConsole = System.Console;

namespace Ledger.Console.Menus;

/// <summary>
/// Loans, existing debts, splits and settlements (of a group, or all of mine): edit, delete,
/// restore and history. Nothing is edited in place; linked people involved confirm every change.
/// </summary>
public sealed class OperationsScreen(LedgerApiClient api, SplitWizard splitWizard)
{
    public async Task RunAsync(Guid? groupId, CancellationToken ct)
    {
        var page = 1;
        var includeHistory = false;

        while (!ct.IsCancellationRequested)
        {
            var result = await api.OperationsAsync(groupId, includeHistory, page, ct);

            ConsoleUi.Header(
                $"LOANS, SPLITS AND SETTLEMENTS · page {result.Page}/{Math.Max(1, result.TotalPages)} ({result.TotalCount})" +
                (includeHistory ? " · with changed and deleted" : ""));

            for (var i = 0; i < result.Items.Count; i++)
                SystemConsole.WriteLine($"{i + 1,3}. {Label(result.Items[i])}");

            if (result.Items.Count == 0)
                SystemConsole.WriteLine("(nothing recorded)");

            SystemConsole.Write("\nnumber = open, n/p = next/previous page, h = show/hide changed and deleted, ENTER = back: ");
            var key = ConsoleUi.ReadLine().Trim().ToLowerInvariant();

            if (key == "n" && page < result.TotalPages)
                page++;
            else if (key == "p" && page > 1)
                page--;
            else if (key == "h")
            {
                includeHistory = !includeHistory;
                page = 1;
            }
            else if (int.TryParse(key, out var number) && number >= 1 && number <= result.Items.Count)
            {
                try
                {
                    await OpenAsync(result.Items[number - 1], ct);
                }
                catch (ApiException ex)
                {
                    ConsoleUi.Error(ex.Message);
                    ConsoleUi.Pause();
                }
            }
            else if (key.Length == 0)
                return;
        }
    }

    public static string Label(OperationDetails o)
    {
        var flags = new List<string>();

        if (o.Status is not ("Completed" or "Pending")) flags.Add(o.Status == "Superseded" ? "changed" : o.Status.ToLowerInvariant());
        if (o.PendingChange is { } change) flags.Add(change.WaitingForMe ? "change waiting for you" : "change pending");
        if (o.IsDisputed) flags.Add("disputed");
        if (o.BlockedReason == "debt.settled") flags.Add("settled");

        var who = o.Kind == "Split"
            ? $"paid by {string.Join(", ", o.Payers.Select(p => p.Name))}, {o.Shares.Count} people"
            : $"{o.FromName} → {o.ToName}";

        return $"{o.OccurredAt.ToLocalTime():yyyy-MM-dd HH:mm} {o.Kind,-11} {ConsoleUi.Money(o.Total, o.Currency),12}  " +
               $"{o.Description ?? who}" + (o.Description is null ? "" : $" ({who})") +
               (o.GroupName is null ? "" : $" [{o.GroupName}]") +
               (flags.Count == 0 ? "" : $" · {string.Join(", ", flags)}");
    }

    private async Task OpenAsync(OperationDetails operation, CancellationToken ct)
    {
        var actions = new List<ScreenAction>();

        if (operation.CanEdit) actions.Add(new("Edit", () => EditAsync(operation, ct)));
        if (operation.CanDelete) actions.Add(new("Delete", () => DeleteAsync(operation, ct)));
        if (operation.CanRestore) actions.Add(new("Restore", () => RestoreAsync(operation, ct)));

        if (operation.BlockedReason == "debt.settled" && operation.SettlementOperationIds.Count > 0)
            actions.Add(new("Undo the settlement (to change it)", async () =>
                await DeleteAsync(await api.OperationDetailsAsync(operation.SettlementOperationIds[0], ct), ct)));

        actions.Add(new("History", () => HistoryAsync(operation.Id, ct)));

        ConsoleUi.Header(Label(operation));

        if (operation.PendingChange is { } pending)
        {
            SystemConsole.WriteLine(
                $"Change pending, asked by {pending.RequestedByName}: \"{pending.Reason}\". Waiting for {string.Join(", ", pending.WaitingFor)}.");
        }

        if (ConsoleUi.Choose("Action", actions, a => a.Label) is { } action)
            await action.Run();
    }

    private async Task EditAsync(OperationDetails operation, CancellationToken ct)
    {
        ChangeResult? result = operation.Kind switch
        {
            "Loan" => await EditLoanAsync(operation, ct),
            "InitialDebt" => await EditInitialDebtAsync(operation, ct),
            "Settlement" => await EditSettlementAsync(operation, ct),
            "Split" => await splitWizard.ReviseAsync(operation, await SplitPeopleAsync(operation, ct), ct),
            _ => null
        };

        if (result is not null)
            await ShowChangeAsync(result, "Change applied.", ct);
        else
            ConsoleUi.Pause();
    }

    private async Task<ChangeResult?> EditLoanAsync(OperationDetails loan, CancellationToken ct)
    {
        Guid? contactId = loan.ContactId;
        Guid? walletId = loan.WalletId;

        if (loan.RecordedByMe)
        {
            if (ConsoleUi.Confirm($"Change who got it (now {loan.ToName})?"))
                contactId = ConsoleUi.Choose("To", await api.ContactsAsync(ct), c => c.Name)?.Id ?? contactId;

            if (ConsoleUi.Confirm("Change the wallet?"))
                walletId = ConsoleUi.Choose("From wallet", await api.WalletsAsync(false, ct), WalletsMenu.Label)?.Id ?? walletId;
        }
        else
        {
            SystemConsole.WriteLine($"Recorded by {loan.RecordedByName}: only they can change who got it or the wallet.");
        }

        return await api.ReviseOperationAsync(loan.Id, new
        {
            amount = ConsoleUi.ReadDecimalOrKeep($"Amount ({loan.Currency})", loan.Total),
            description = ConsoleUi.ReadTextOrKeep("Description", loan.Description),
            occurredAt = ReadWhen(loan),
            contactId = loan.RecordedByMe ? contactId : null,
            walletId = loan.RecordedByMe ? walletId : null,
            reason = ConsoleUi.ReadReason()
        }, ct);
    }

    private async Task<ChangeResult?> EditInitialDebtAsync(OperationDetails debt, CancellationToken ct)
    {
        Guid? contactId = debt.ContactId;
        var direction = debt.Direction;
        var currency = debt.Currency;

        if (debt.RecordedByMe)
        {
            if (ConsoleUi.Confirm($"Change the contact (now {debt.CounterpartyName})?"))
                contactId = ConsoleUi.Choose("Contact", await api.ContactsAsync(ct), c => c.Name)?.Id ?? contactId;

            if (ConsoleUi.Confirm($"Change who owed whom (now {(direction == "IOwe" ? "I owed them" : "they owed me")})?"))
                direction = direction == "IOwe" ? "TheyOweMe" : "IOwe";

            if (ConsoleUi.Confirm($"Change the currency (now {currency})?"))
                currency = ConsoleUi.Choose("Currency", ["EUR", "USD", "ARS"], c => c) ?? currency;
        }
        else
        {
            SystemConsole.WriteLine($"Recorded by {debt.RecordedByName}: only they can change who owes whom or the currency.");
        }

        return await api.ReviseOperationAsync(debt.Id, new
        {
            amount = ConsoleUi.ReadDecimalOrKeep($"Amount ({currency})", debt.Total),
            currency,
            description = ConsoleUi.ReadTextOrKeep("Description", debt.Description),
            occurredAt = ReadWhen(debt),
            contactId = debt.RecordedByMe ? contactId : null,
            direction = debt.RecordedByMe ? direction : null,
            reason = ConsoleUi.ReadReason()
        }, ct);
    }

    private async Task<ChangeResult?> EditSettlementAsync(OperationDetails settlement, CancellationToken ct)
    {
        SystemConsole.WriteLine("The amount comes from the debts it paid. To pay a different amount, delete it and settle again.");
        var walletId = settlement.WalletId;

        if (settlement.RecordedByMe && settlement.Total > 0 && (walletId is null || ConsoleUi.Confirm("Change the wallet?")))
        {
            var wallets = (await api.WalletsAsync(false, ct)).Where(w => w.Currency == settlement.Currency).ToList();
            walletId = ConsoleUi.Choose("Wallet", wallets, WalletsMenu.Label)?.Id ?? walletId;
        }

        return await api.ReviseOperationAsync(settlement.Id, new
        {
            description = ConsoleUi.ReadTextOrKeep("Description", settlement.Description),
            occurredAt = ReadWhen(settlement),
            walletId = settlement.RecordedByMe ? walletId : null,
            reason = ConsoleUi.ReadReason()
        }, ct);
    }

    private async Task DeleteAsync(OperationDetails operation, CancellationToken ct)
    {
        SystemConsole.WriteLine(operation.Kind == "Settlement"
            ? "The debts it paid will be open again. It stays in the history."
            : "It stays in the history and can be restored. Linked people involved have to confirm it.");

        if (!ConsoleUi.Confirm($"Delete this {operation.Kind}?"))
            return;

        var result = await api.DeleteOperationAsync(operation.Id, ConsoleUi.ReadReason(), ct);
        await ShowChangeAsync(result, "Deleted.", ct);
    }

    private async Task RestoreAsync(OperationDetails operation, CancellationToken ct)
    {
        var result = await api.RestoreOperationAsync(operation.Id, ConsoleUi.ReadReason(), ct);
        await ShowChangeAsync(result, "Restored.", ct);
    }

    public async Task HistoryAsync(Guid operationId, CancellationToken ct)
    {
        var history = await api.OperationHistoryAsync(operationId, ct);

        SystemConsole.WriteLine("Versions:");
        foreach (var version in history.Versions)
            SystemConsole.WriteLine($"  v{version.Revision} {Label(version)}");

        SystemConsole.WriteLine("\nChanges:");
        if (history.Changes.Count == 0)
            SystemConsole.WriteLine("  (it has not been changed)");

        foreach (var change in history.Changes)
        {
            SystemConsole.WriteLine(
                $"  {change.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm} {change.Kind} by {change.RequestedByName}: \"{change.Reason}\" · {change.Status}" +
                (change.Disputed ? " · disputed" : "") + (change.FailureReason is { } f ? $" ({f})" : ""));

            foreach (var approval in change.Approvals)
                SystemConsole.WriteLine($"      {approval.Name}: {approval.Status}" + (approval.Reason is { } r ? $" — \"{r}\"" : ""));
        }

        ConsoleUi.Pause();
    }

    /// <summary>A change is confirmed by linked users first, or applied to the books asynchronously.</summary>
    public async Task ShowChangeAsync(ChangeResult result, string success, CancellationToken ct)
    {
        if (result.Status == "Pending")
        {
            ConsoleUi.Success($"Sent for confirmation to {string.Join(", ", result.WaitingFor)}. Nothing changes until they answer.");
            return;
        }

        var (status, failure) = await api.WaitForChangeAsync(result, ct);

        switch (status)
        {
            case "Applied":
                ConsoleUi.Success(success);
                break;
            case "Failed":
                ConsoleUi.Error($"The change could not be applied: {failure}");
                ConsoleUi.Pause();
                break;
            default:
                ConsoleUi.Success("Still updating the books. Check again in a moment.");
                break;
        }
    }

    /// <summary>The split's own people, plus my contacts (if I recorded it) or the group's members.</summary>
    private async Task<IReadOnlyList<Person>> SplitPeopleAsync(OperationDetails split, CancellationToken ct)
    {
        var me = await api.MeAsync(ct);
        var people = new Dictionary<Guid, Person>();

        if (split.GroupId is { } groupId)
        {
            var group = (await api.GroupsAsync(ct)).Single(g => g.Id == groupId);
            foreach (var member in group.Members)
                people[member.PersonId] = new Person(member.PersonId, member.DisplayName, member.IsMe);
        }
        else
        {
            foreach (var person in split.Payers.Concat(split.Shares))
                people[person.PersonId] = new Person(person.PersonId, person.Name, person.PersonId == me.PersonId);

            if (split.RecordedByMe)
            {
                foreach (var contact in await api.ContactsAsync(ct))
                    people.TryAdd(contact.PersonId, new Person(contact.PersonId, contact.Name, false));
            }
        }

        people[me.PersonId] = new Person(me.PersonId, me.DisplayName, true);

        return people.Values.OrderByDescending(p => p.IsMe).ToList();
    }

    private static DateTimeOffset ReadWhen(OperationDetails operation)
        => ConsoleUi.ReadWhen($"When [{operation.OccurredAt.ToLocalTime():yyyy-MM-dd HH:mm}] (ENTER = keep): ", operation.OccurredAt);
}

internal sealed record ScreenAction(string Label, Func<Task> Run);
