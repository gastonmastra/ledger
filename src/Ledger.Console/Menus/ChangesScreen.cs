using Ledger.Console.Api;
using SystemConsole = System.Console;

namespace Ledger.Console.Menus;

/// <summary>
/// Changes of shared operations waiting for my answer. Accepting updates my books; rejecting keeps
/// my previous version and marks the operation as disputed.
/// </summary>
public sealed class ChangesScreen(LedgerApiClient api, OperationsScreen operations)
{
    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var changes = await api.ChangesAsync(all: false, ct);
            ConsoleUi.Header($"CHANGES TO REVIEW ({changes.Count})");

            if (changes.Count == 0)
            {
                SystemConsole.WriteLine("Nothing waiting for you.");
                ConsoleUi.Pause();
                return;
            }

            for (var i = 0; i < changes.Count; i++)
                Show(i + 1, changes[i]);

            if (ConsoleUi.Choose("Change to answer (0 = back)", changes, c => $"{c.OperationKind} · {c.Kind} by {c.RequestedByName}", allowNone: true)
                is not { } change)
                return;

            var answer = ConsoleUi.Choose("Answer", ["Accept (my books get the new version)", "Reject (my books keep the current one)", "History"], a => a);

            try
            {
                switch (answer?[0])
                {
                    case 'A':
                        await RespondAsync(change, true, null, ct);
                        break;
                    case 'R':
                        var reason = ConsoleUi.ReadText("Why? (optional): ", optional: true);
                        await RespondAsync(change, false, reason.Length == 0 ? null : reason, ct);
                        break;
                    case 'H':
                        await operations.HistoryAsync(change.OperationId, ct);
                        break;
                }
            }
            catch (ApiException ex)
            {
                ConsoleUi.Error(ex.Message);
                ConsoleUi.Pause();
            }
        }
    }

    private async Task RespondAsync(OperationChange change, bool accept, string? reason, CancellationToken ct)
    {
        var result = await api.RespondToChangeAsync(change.Id, accept, reason, ct);

        if (result.Status == "Pending")
            ConsoleUi.Success($"Answer recorded. Still waiting for {string.Join(", ", result.WaitingFor)}.");
        else
            await operations.ShowChangeAsync(result, "Thanks, your answer was recorded.", ct);
    }

    private static void Show(int number, OperationChange change)
    {
        SystemConsole.WriteLine($"{number}. {change.OperationKind} · {change.Kind} asked by {change.RequestedByName}: \"{change.Reason}\"");
        SystemConsole.WriteLine($"     now:      {Snapshot(change.Current)}");
        SystemConsole.WriteLine(change.Proposed is { } proposed
            ? $"     proposed: {Snapshot(proposed)}"
            : "     proposed: delete it (it stays in the history)");
    }

    private static string Snapshot(ChangeSnapshot s)
        => $"{ConsoleUi.Money(s.Total, s.Currency)} on {s.OccurredAt.ToLocalTime():yyyy-MM-dd HH:mm} {s.Description}" +
           (s.Shares.Count == 0 ? $" ({s.FromName} → {s.ToName})" : $" (shares: {string.Join(", ", s.Shares.Select(x => $"{x.Name} {ConsoleUi.Amount(x.Amount)}"))})");
}
