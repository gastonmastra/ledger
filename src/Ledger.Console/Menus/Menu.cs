using Ledger.Console.Api;

namespace Ledger.Console.Menus;

/// <summary>A screen with options that loops until the user goes back.</summary>
public abstract class Menu
{
    protected abstract string Title { get; }

    protected abstract IReadOnlyList<(string Label, Func<CancellationToken, Task> Action)> Actions { get; }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var actions = Actions;
            var options = actions
                .Select((a, i) => new Option(i + 1, a.Label))
                .Append(new Option(actions.Count + 1, "Back"))
                .ToArray();

            var option = ConsoleUi.ShowMenu(Title, options);

            if (option.Order == actions.Count + 1)
                return;

            ConsoleUi.Header($"{Title} · {option.Description}");

            try
            {
                await actions[option.Order - 1].Action(cancellationToken);
            }
            catch (ApiException ex)
            {
                ConsoleUi.Error(ex.Message);
                ConsoleUi.Pause();
            }
            catch (HttpRequestException ex)
            {
                ConsoleUi.Error($"Cannot reach the API: {ex.Message}");
                ConsoleUi.Pause();
            }
        }
    }

    protected static void ShowOperation(Operation operation, string success)
    {
        switch (operation.Status)
        {
            case "Completed":
                ConsoleUi.Success(success);
                break;
            case "Rejected":
                ConsoleUi.Error($"Rejected: {operation.RejectionReason}");
                ConsoleUi.Pause();
                break;
            default:
                ConsoleUi.Success($"Still being recorded (operation {operation.Id}). Check the debts screen in a moment.");
                break;
        }
    }
}
