using Ledger.Console.Api;
using SystemConsole = System.Console;

namespace Ledger.Console.Menus;

/// <summary>Recent edits, deletions and restores: of my transactions and of operations I share.</summary>
public sealed class ActivityScreen(LedgerApiClient api)
{
    public async Task ShowAsync(CancellationToken ct)
    {
        var ledger = await api.TransactionActivityAsync(30, ct);
        var debts = await api.DebtActivityAsync(30, ct);

        var rows = ledger.Items
            .Select(a => (a.At, Line: $"{a.Kind,-15} {a.Action,-8} {a.Description} — \"{a.Reason}\"" +
                                      (a.ChangedByMe || a.ChangedByName is null ? "" : $" by {a.ChangedByName}")))
            .Concat(debts.Items.Select(c => (At: c.CreatedAt,
                Line: $"{c.OperationKind,-15} {c.Kind,-8} {(c.Proposed ?? c.Current).Description} — \"{c.Reason}\"" +
                      (c.RequestedByMe ? "" : $" by {c.RequestedByName}") + $" · {c.Status}" + (c.Disputed ? " · disputed" : ""))))
            .OrderByDescending(r => r.At)
            .ToList();

        ConsoleUi.Header("ACTIVITY");

        foreach (var (at, line) in rows)
            SystemConsole.WriteLine($"{at.ToLocalTime():yyyy-MM-dd HH:mm}  {line}");

        if (rows.Count == 0)
            SystemConsole.WriteLine("Nothing has been changed yet.");

        ConsoleUi.Pause();
    }
}
