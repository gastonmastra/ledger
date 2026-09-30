using Ledger.Console.Api;

namespace Ledger.Console.Menus;

public sealed class CategoriesMenu(LedgerApiClient api) : Menu
{
    protected override string Title => "CATEGORIES";

    protected override IReadOnlyList<(string, Func<CancellationToken, Task>)> Actions =>
    [
        ("List categories", ListAsync),
        ("Create category", CreateAsync),
        ("Rename category", RenameAsync),
        ("Archive category", ArchiveAsync)
    ];

    private async Task ListAsync(CancellationToken ct)
    {
        ConsoleUi.Table(await api.CategoriesAsync(ct), ("Name", c => c.Name), ("Type", c => c.Type));
        ConsoleUi.Pause();
    }

    private async Task CreateAsync(CancellationToken ct)
    {
        var name = ConsoleUi.ReadText("Name: ");
        var type = ConsoleUi.Choose("Type", ["Expense", "Income"], t => t)!;

        await api.CreateCategoryAsync(name, type, ct);
        ConsoleUi.Success($"{type} category '{name}' created.");
    }

    private async Task RenameAsync(CancellationToken ct)
    {
        if (ConsoleUi.Choose("Category", await api.CategoriesAsync(ct), c => $"{c.Name} ({c.Type})") is not { } category)
        {
            ConsoleUi.Pause();
            return;
        }

        await api.RenameCategoryAsync(category.Id, ConsoleUi.ReadText("New name: "), ct);
        ConsoleUi.Success("Category renamed.");
    }

    private async Task ArchiveAsync(CancellationToken ct)
    {
        if (ConsoleUi.Choose("Category", await api.CategoriesAsync(ct), c => $"{c.Name} ({c.Type})") is not { } category)
        {
            ConsoleUi.Pause();
            return;
        }

        await api.ArchiveCategoryAsync(category.Id, ct);
        ConsoleUi.Success($"Category '{category.Name}' archived.");
    }
}
