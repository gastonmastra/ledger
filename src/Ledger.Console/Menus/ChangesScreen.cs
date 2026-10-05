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
        await AnswerLinkRequestsAsync(ct);

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

    /// <summary>Somebody wants to link one of their contacts (that already has debts) to my account.</summary>
    private async Task AnswerLinkRequestsAsync(CancellationToken ct)
    {
        var requests = (await api.LinkRequestsAsync(includeClosed: false, ct))
            .Where(r => r.Direction == "Incoming" && r.Status == "Pending")
            .ToList();

        foreach (var request in requests)
        {
            ConsoleUi.Header("LINK REQUEST");
            SystemConsole.WriteLine(
                $"{request.OwnerName} had you as the contact \"{request.ContactName}\" before you joined and wants to link it to your account.");

            var preview = await api.LinkPreviewAsync(request.Id, ct);
            SystemConsole.WriteLine("\nIf you accept, your books get:");

            if (preview.Balances.Count == 0)
                SystemConsole.WriteLine("  nothing open: you are even.");

            foreach (var balance in preview.Balances)
            {
                SystemConsole.WriteLine(balance.Amount < 0
                    ? $"  {balance.Name}: you owe {ConsoleUi.Money(-balance.Amount, balance.Currency)}"
                    : $"  {balance.Name}: owes you {ConsoleUi.Money(balance.Amount, balance.Currency)}");
            }

            SystemConsole.WriteLine(
                $"\n{preview.Operations} operation(s) copied as existing debts (neither income nor expense), with their original dates.");

            var answer = ConsoleUi.Choose("Answer", ["Accept", "Reject", "Later"], a => a);

            try
            {
                switch (answer)
                {
                    case "Accept":
                        await api.AcceptLinkRequestAsync(request.Id, ct);
                        var result = await api.WaitForLinkAsync(request.Id, ct);

                        if (result?.Status == "Applied")
                            ConsoleUi.Success("Linked. The shared history is now in your books.");
                        else if (result?.Status == "Failed")
                        {
                            ConsoleUi.Error($"The link could not be applied: {result.FailureReason}");
                            ConsoleUi.Pause();
                        }
                        else
                            ConsoleUi.Success("It is taking longer than usual. Check again in a moment.");
                        break;
                    case "Reject":
                        await api.RejectLinkRequestAsync(request.Id, ct);
                        ConsoleUi.Success("Rejected. Nothing was copied into your books.");
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
}
