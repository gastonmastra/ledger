using Ledger.Console.Api;

namespace Ledger.Console.Menus;

public sealed class ContactsMenu(LedgerApiClient api) : Menu
{
    protected override string Title => "CONTACTS";

    protected override IReadOnlyList<(string, Func<CancellationToken, Task>)> Actions =>
    [
        ("List contacts", ListAsync),
        ("Add contact", AddAsync),
        ("Link contact to a registered user", LinkAsync)
    ];

    private async Task ListAsync(CancellationToken ct)
    {
        ConsoleUi.Table(await api.ContactsAsync(ct),
            ("Name", c => c.Name),
            ("Email", c => c.Email),
            ("Registered user", c => c.IsLinked ? "yes" : "no"),
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
        var candidates = (await api.ContactsAsync(ct)).Where(c => !c.IsLinked).ToList();

        if (ConsoleUi.Choose("Contact", candidates, c => c.Name) is not { } contact)
        {
            ConsoleUi.Pause();
            return;
        }

        await api.LinkContactAsync(contact.Id, ConsoleUi.ReadText("Email of the registered user: "), ct);
        ConsoleUi.Success($"{contact.Name} is now linked.");
    }
}
