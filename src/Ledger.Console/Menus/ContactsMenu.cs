using Ledger.Console.Api;

namespace Ledger.Console.Menus;

public sealed class ContactsMenu(LedgerApiClient api) : Menu
{
    protected override string Title => "CONTACTS";

    protected override IReadOnlyList<(string, Func<CancellationToken, Task>)> Actions =>
    [
        ("List contacts", ListAsync),
        ("Add contact", AddAsync),
        ("Link contact to a registered user", LinkAsync),
        ("Cancel a link request", CancelLinkAsync)
    ];

    private async Task ListAsync(CancellationToken ct)
    {
        ConsoleUi.Table(await api.ContactsAsync(ct),
            ("Name", c => c.Name),
            ("Email", c => c.Email),
            ("Registered user", c => c.IsLinked ? "yes" : c.PendingLinkRequestId is not null ? "waiting for them to accept" : "no"),
            ("Added", c => c.Origin == "Mirrored" ? "automatically" : "by me"));
        ConsoleUi.Pause();
    }

    private async Task AddAsync(CancellationToken ct)
    {
        var name = ConsoleUi.ReadText("Name: ");
        var email = ConsoleUi.ReadText("Email (optional; links the contact if it belongs to a registered user): ", optional: true);

        var contact = await api.CreateContactAsync(name, email.Length == 0 ? null : email, ct);

        ConsoleUi.Success(contact.IsLinked
            ? $"{contact.Name} added and linked: shared debts will also show up in their Ledger."
            : $"{contact.Name} added.");
    }

    private async Task LinkAsync(CancellationToken ct)
    {
        var candidates = (await api.ContactsAsync(ct)).Where(c => !c.IsLinked && c.PendingLinkRequestId is null).ToList();

        if (ConsoleUi.Choose("Contact", candidates, c => c.IsInUse ? $"{c.Name} (has debts or groups: they must accept)" : c.Name) is not { } contact)
        {
            ConsoleUi.Pause();
            return;
        }

        var linked = await api.LinkContactAsync(contact.Id, ConsoleUi.ReadText("Email of the registered user: "), ct);

        ConsoleUi.Success(linked.PendingLinkRequestId is not null
            ? $"Request sent. Once they accept it, your history with {contact.Name} is copied into their account and you share everything from then on."
            : $"{contact.Name} is now linked.");
    }

    private async Task CancelLinkAsync(CancellationToken ct)
    {
        var pending = (await api.ContactsAsync(ct)).Where(c => c.PendingLinkRequestId is not null).ToList();

        if (ConsoleUi.Choose("Contact", pending, c => c.Name) is not { } contact)
        {
            ConsoleUi.Pause();
            return;
        }

        await api.CancelLinkRequestAsync(contact.PendingLinkRequestId!.Value, ct);
        ConsoleUi.Success("Link request cancelled.");
    }
}
