using Ledger.Console.Api;
using SystemConsole = System.Console;

namespace Ledger.Console.Menus;

/// <summary>The books lock date: nothing on or before it can be recorded, edited, deleted or restored.</summary>
public sealed class SettingsMenu(LedgerApiClient api) : Menu
{
    protected override string Title => "SETTINGS";

    protected override IReadOnlyList<(string, Func<CancellationToken, Task>)> Actions =>
    [
        ("Close my books up to a date", LockAsync),
        ("Unlock everything", UnlockAsync)
    ];

    private async Task LockAsync(CancellationToken ct)
    {
        var current = await api.BooksLockAsync(ct);

        SystemConsole.WriteLine(current.LockedUntil is { } until
            ? $"Closed up to {until.ToLocalTime():yyyy-MM-dd HH:mm}."
            : "Nothing is locked.");
        SystemConsole.WriteLine(
            "Nothing dated on or before the lock date can be recorded, edited, deleted or restored, and shared\n" +
            "operations that would touch it are refused. You can move the date back at any time.");

        var date = ConsoleUi.ReadWhen("Closed up to (yyyy-MM-dd HH:mm, ENTER = now): ");
        await api.SetBooksLockAsync(date, ct);
        ConsoleUi.Success($"Books closed up to {date.ToLocalTime():yyyy-MM-dd HH:mm}.");
    }

    private async Task UnlockAsync(CancellationToken ct)
    {
        await api.SetBooksLockAsync(null, ct);
        ConsoleUi.Success("Everything is open again.");
    }
}
