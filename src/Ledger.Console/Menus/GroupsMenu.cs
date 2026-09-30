using Ledger.Console.Api;
using SystemConsole = System.Console;

namespace Ledger.Console.Menus;

public sealed class GroupsMenu(LedgerApiClient api, SplitWizard splitWizard) : Menu
{
    protected override string Title => "GROUPS";

    protected override IReadOnlyList<(string, Func<CancellationToken, Task>)> Actions =>
    [
        ("List groups", ListAsync),
        ("Create group", CreateAsync),
        ("Add member", AddMemberAsync),
        ("Remove member", RemoveMemberAsync),
        ("Record group expense", ExpenseAsync),
        ("Balances and suggested payments", BalancesAsync),
        ("Settle a suggested payment", SettleAsync)
    ];

    private async Task ListAsync(CancellationToken ct)
    {
        foreach (var group in await api.GroupsAsync(ct))
            SystemConsole.WriteLine($"{group.Name}: {string.Join(", ", group.Members.Select(Name))}");

        ConsoleUi.Pause();
    }

    private async Task CreateAsync(CancellationToken ct)
    {
        var name = ConsoleUi.ReadText("Group name: ");
        var contacts = await api.ContactsAsync(ct);
        var members = new List<Guid>();

        while (ConsoleUi.Choose("Add a contact (0 = done)", contacts.Where(c => !members.Contains(c.Id)).ToList(), c => c.Name, allowNone: true) is { } contact)
            members.Add(contact.Id);

        var group = await api.CreateGroupAsync(name, members, ct);
        ConsoleUi.Success($"Group '{group.Name}' created with {group.Members.Count} members.");
    }

    private async Task AddMemberAsync(CancellationToken ct)
    {
        if (await PickGroupAsync(ct) is not { } group)
            return;

        var candidates = (await api.ContactsAsync(ct))
            .Where(c => group.Members.All(m => m.PersonId != c.PersonId))
            .ToList();

        if (ConsoleUi.Choose("Contact", candidates, c => c.Name) is not { } contact)
        {
            ConsoleUi.Pause();
            return;
        }

        await api.AddGroupMemberAsync(group.Id, contact.Id, ct);
        ConsoleUi.Success($"{contact.Name} joined {group.Name}.");
    }

    private async Task RemoveMemberAsync(CancellationToken ct)
    {
        if (await PickGroupAsync(ct) is not { } group
            || ConsoleUi.Choose("Member", group.Members.Where(m => !m.IsMe).ToList(), m => m.DisplayName) is not { } member)
        {
            ConsoleUi.Pause();
            return;
        }

        await api.RemoveGroupMemberAsync(group.Id, member.PersonId, ct);
        ConsoleUi.Success($"{member.DisplayName} removed.");
    }

    private async Task ExpenseAsync(CancellationToken ct)
    {
        if (await PickGroupAsync(ct) is not { } group)
            return;

        var people = group.Members.Select(m => new Person(m.PersonId, m.DisplayName, m.IsMe)).ToList();

        if (await splitWizard.RunAsync(group.Id, people, ct) is { } operation)
            ShowOperation(operation, "Group expense recorded.");
        else
            ConsoleUi.Pause();
    }

    private async Task BalancesAsync(CancellationToken ct)
    {
        if (await PickGroupAsync(ct) is not { } group)
            return;

        var balances = await api.GroupBalancesAsync(group.Id, ct);

        SystemConsole.WriteLine("\nBalances (+ the group owes them, − they owe the group):");
        ConsoleUi.Table(balances.Balances,
            ("Member", b => b.Name),
            ("Balance", b => ConsoleUi.Money(b.Balance, b.Currency)));

        SystemConsole.WriteLine("\nSuggested payments (simplified):");
        ConsoleUi.Table(balances.SuggestedPayments,
            ("From", p => p.FromName),
            ("To", p => p.ToName),
            ("Amount", p => ConsoleUi.Money(p.Amount, p.Currency)));

        SystemConsole.WriteLine("\nWho owes whom (per expense):");
        ConsoleUi.Table(balances.Debts,
            ("Debt", d => d.CounterpartyName),
            ("Amount", d => ConsoleUi.Money(d.Amount, d.Currency)),
            ("For", d => d.Description));

        ConsoleUi.Pause();
    }

    private async Task SettleAsync(CancellationToken ct)
    {
        if (await PickGroupAsync(ct) is not { } group)
            return;

        var balances = await api.GroupBalancesAsync(group.Id, ct);

        if (ConsoleUi.Choose("Payment made", balances.SuggestedPayments,
                p => $"{p.FromName} → {p.ToName}: {ConsoleUi.Money(p.Amount, p.Currency)}") is not { } payment)
        {
            ConsoleUi.Pause();
            return;
        }

        var me = group.Members.Single(m => m.IsMe).PersonId;
        Guid? walletId = null;

        if (payment.FromPersonId == me || payment.ToPersonId == me)
        {
            var wallets = (await api.WalletsAsync(false, ct)).Where(w => w.Currency == payment.Currency).ToList();
            walletId = ConsoleUi.Choose(payment.FromPersonId == me ? "Paid from" : "Received in", wallets, WalletsMenu.Label)?.Id;

            if (walletId is null)
            {
                ConsoleUi.Pause();
                return;
            }
        }

        var operation = await api.SettleAsync(new
        {
            scope = "GroupPair",
            groupId = group.Id,
            fromPersonId = payment.FromPersonId,
            toPersonId = payment.ToPersonId,
            currency = payment.Currency,
            walletId,
            occurredAt = ConsoleUi.ReadWhen()
        }, ct);

        ShowOperation(await api.WaitForOperationAsync(operation, ct), "Payment recorded.");
    }

    private async Task<Group?> PickGroupAsync(CancellationToken ct)
    {
        var group = ConsoleUi.Choose("Group", await api.GroupsAsync(ct), g => g.Name);

        if (group is null)
            ConsoleUi.Pause();

        return group;
    }

    private static string Name(GroupMember member) => member.IsMe ? "Me" : member.DisplayName;
}
