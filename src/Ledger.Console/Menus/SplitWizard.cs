using Ledger.Console.Api;
using SystemConsole = System.Console;

namespace Ledger.Console.Menus;

public sealed record Person(Guid PersonId, string Name, bool IsMe);

/// <summary>Everything a split needs.</summary>
public sealed record SplitInput(
    string Description, decimal Total, string Currency, string Method, IReadOnlyList<SplitPayer> Payers,
    IReadOnlyList<SplitParticipant> Participants, Guid? CategoryId, DateTimeOffset OccurredAt);

/// <summary>Asks for everything a split expense needs: payers, participants and how to share.</summary>
public sealed class SplitWizard(LedgerApiClient api)
{
    private static readonly string[] Currencies = ["EUR", "USD", "ARS"];

    public async Task<Operation?> RunAsync(Guid? groupId, IReadOnlyList<Person> people, CancellationToken ct)
    {
        if (await CollectAsync(people, null, ct) is not { } input)
            return null;

        var operation = await api.SplitAsync(
            groupId, input.Total, input.Currency, input.Method, input.Payers, input.Participants, input.CategoryId,
            input.Description, input.OccurredAt, ct);

        return await api.WaitForOperationAsync(operation, ct);
    }

    /// <summary>Asks for the new version of a split (everything again, current values shown) and why.</summary>
    public async Task<ChangeResult?> ReviseAsync(OperationDetails split, IReadOnlyList<Person> people, CancellationToken ct)
    {
        SystemConsole.WriteLine(
            $"Now: {split.Description} · {ConsoleUi.Money(split.Total, split.Currency)} · paid by " +
            string.Join(", ", split.Payers.Select(p => $"{p.Name} {ConsoleUi.Amount(p.Amount)}")) + " · shares " +
            string.Join(", ", split.Shares.Select(p => $"{p.Name} {ConsoleUi.Amount(p.Amount)}")));

        if (!split.RecordedByMe)
            SystemConsole.WriteLine($"Recorded by {split.RecordedByName}: only they can change the wallets, category or currency.");

        if (await CollectAsync(people, split, ct) is not { } input)
            return null;

        var reason = ConsoleUi.ReadReason();

        return await api.ReviseOperationAsync(split.Id, new
        {
            reason,
            occurredAt = input.OccurredAt,
            description = input.Description,
            amount = input.Total,
            currency = input.Currency,
            method = input.Method,
            payers = input.Payers,
            participants = input.Participants,
            categoryId = input.CategoryId
        }, ct);
    }

    private async Task<SplitInput?> CollectAsync(IReadOnlyList<Person> people, OperationDetails? edit, CancellationToken ct)
    {
        // Wallets, category and currency belong to whoever recorded the split.
        var mine = edit is null || edit.RecordedByMe;
        var description = edit is null
            ? ConsoleUi.ReadText("What was it? ")
            : ConsoleUi.ReadTextOrKeep("What was it?", edit.Description) ?? edit.Description ?? "";
        var total = edit is null ? ConsoleUi.ReadDecimal("Total amount: ") : ConsoleUi.ReadDecimalOrKeep("Total amount", edit.Total);

        // Payers
        var payers = new List<SplitPayer>();
        Wallet? myWallet = null;
        var remaining = total;

        SystemConsole.WriteLine("\nWho paid? Add payers until the total is covered.");

        while (remaining > 0)
        {
            var available = people.Where(p => payers.All(x => x.PersonId != p.PersonId)).ToList();

            if (ConsoleUi.Choose($"Payer (remaining {ConsoleUi.Amount(remaining)})", available, p => p.IsMe ? "Me" : p.Name) is not { } payer)
                return null;

            var amount = ConsoleUi.ReadOptionalDecimal($"Amount paid by {(payer.IsMe ? "me" : payer.Name)} (ENTER = {ConsoleUi.Amount(remaining)}): ") ?? remaining;

            if (amount <= 0 || amount > remaining)
            {
                ConsoleUi.Error($"Enter an amount between 0.01 and {ConsoleUi.Amount(remaining)}.");
                continue;
            }

            if (payer.IsMe && mine)
            {
                myWallet = ConsoleUi.Choose("Wallet I paid from", await api.WalletsAsync(false, ct), WalletsMenu.Label);

                if (myWallet is null)
                    return null;
            }

            payers.Add(new SplitPayer(payer.PersonId, amount, payer.IsMe && mine ? myWallet!.Id : null));
            remaining -= amount;
        }

        var currency = myWallet?.Currency ?? (mine ? ConsoleUi.Choose("Currency", Currencies, c => c)! : edit!.Currency);

        // Participants and shares
        SystemConsole.WriteLine("\nWho took part? (numbers separated by commas, ENTER = everybody)");

        for (var i = 0; i < people.Count; i++)
            SystemConsole.WriteLine($"  {i + 1}. {(people[i].IsMe ? "Me" : people[i].Name)}");

        var selection = ConsoleUi.ReadText("Participants: ", optional: true);
        var participants = selection.Length == 0
            ? people.ToList()
            : selection.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(x => int.TryParse(x, out var n) && n >= 1 && n <= people.Count ? people[n - 1] : null)
                .OfType<Person>()
                .Distinct()
                .ToList();

        if (participants.Count == 0)
        {
            ConsoleUi.Error("Nobody selected.");
            return null;
        }

        var method = ConsoleUi.Choose("How to split", ["Equal", "Custom", "Percent"], m => m)!;

        var shares = participants
            .Select(p => new SplitParticipant(
                p.PersonId,
                method switch
                {
                    "Custom" => ConsoleUi.ReadDecimal($"Amount for {(p.IsMe ? "me" : p.Name)}: "),
                    "Percent" => ConsoleUi.ReadDecimal($"Percentage for {(p.IsMe ? "me" : p.Name)}: "),
                    _ => null
                }))
            .ToList();

        Guid? categoryId = null;

        if (participants.Any(p => p.IsMe) && mine)
        {
            var categories = (await api.CategoriesAsync(ct)).Where(c => c.Type == "Expense").ToList();

            if (categories.Count > 0)
                categoryId = ConsoleUi.Choose("Category of my share (0 = uncategorized)", categories, c => c.Name, allowNone: true)?.Id;
        }

        var when = edit is null
            ? ConsoleUi.ReadWhen()
            : ConsoleUi.ReadWhen($"When [{edit.OccurredAt.ToLocalTime():yyyy-MM-dd HH:mm}] (ENTER = keep): ", edit.OccurredAt);

        return new SplitInput(description, total, currency, method, payers, shares, categoryId, when);
    }
}
