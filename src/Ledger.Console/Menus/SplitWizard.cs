using Ledger.Console.Api;
using SystemConsole = System.Console;

namespace Ledger.Console.Menus;

public sealed record Person(Guid PersonId, string Name, bool IsMe);

/// <summary>Asks for everything a split expense needs: payers, participants and how to share.</summary>
public sealed class SplitWizard(LedgerApiClient api)
{
    private static readonly string[] Currencies = ["EUR", "USD", "ARS"];

    public async Task<Operation?> RunAsync(Guid? groupId, IReadOnlyList<Person> people, CancellationToken ct)
    {
        var description = ConsoleUi.ReadText("What was it? ");
        var total = ConsoleUi.ReadDecimal("Total amount: ");

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

            if (payer.IsMe)
            {
                myWallet = ConsoleUi.Choose("Wallet I paid from", await api.WalletsAsync(false, ct), WalletsMenu.Label);

                if (myWallet is null)
                    return null;
            }

            payers.Add(new SplitPayer(payer.PersonId, amount, payer.IsMe ? myWallet!.Id : null));
            remaining -= amount;
        }

        var currency = myWallet?.Currency ?? ConsoleUi.Choose("Currency", Currencies, c => c)!;

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

        if (participants.Any(p => p.IsMe))
        {
            var categories = (await api.CategoriesAsync(ct)).Where(c => c.Type == "Expense").ToList();

            if (categories.Count > 0)
                categoryId = ConsoleUi.Choose("Category of my share (0 = uncategorized)", categories, c => c.Name, allowNone: true)?.Id;
        }

        var when = ConsoleUi.ReadWhen();
        var operation = await api.SplitAsync(groupId, total, currency, method, payers, shares, categoryId, description, when, ct);

        return await api.WaitForOperationAsync(operation, ct);
    }
}
