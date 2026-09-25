using System.Globalization;
using Ledger.Console.Menus;
using SystemConsole = System.Console;

namespace Ledger.Console;

public static class ConsoleUi
{
    private const int Width = 38;

    public static Option ShowMenu(
        string title,
        Option[] options)
    {
        SystemConsole.Clear();

        Header(title);
        ShowOptions(options);

        return ReadOption(options);
    }

    public static void Header(string title)
    {
        SystemConsole.WriteLine(new string('=', Width));
        SystemConsole.WriteLine(CenterText(title, Width));
        SystemConsole.WriteLine(new string('=', Width));
        SystemConsole.WriteLine();
    }

    private static void ShowOptions(Option[] options)
    {
        foreach (var option in options)
        {
            SystemConsole.WriteLine(
                $"{option.Order}. {option.Description}");
        }

        SystemConsole.WriteLine();
    }

    public static Option ReadOption(
        Option[] options, 
        string systemPropmt =  "Choose an option: ",
        string errorMessage = "Invalid option.")
    {
        while (true)
        {
            SystemConsole.Write(systemPropmt);

            if (int.TryParse(SystemConsole.ReadLine(), out var selected))
            {
                var option = options
                    .FirstOrDefault(x => x.Order == selected);

                if (option is not null)
                    return option;
            }

            ShowError(errorMessage);
        }
    }

    private static string CenterText(string text, int width)
    {
        if (text.Length >= width)
            return text;

        var leftPadding = (width - text.Length) / 2;

        return text
            .PadLeft(leftPadding + text.Length)
            .PadRight(width);
    }

    public static void ShowError(string error)
    {
        SystemConsole.ForegroundColor = ConsoleColor.Red;
        SystemConsole.WriteLine($"\n[ERROR] {error}");
        SystemConsole.ResetColor();

        SystemConsole.WriteLine("Press ENTER to continue...");
        SystemConsole.ReadLine();
    }

    public static T Read<T>(string message) 
    { 
        while (true) 
        { 
            SystemConsole.Write(message); 
            var input = SystemConsole.ReadLine(); 
            if (input is null) continue; 
            try 
            { 
                if (typeof(T) == typeof(Guid)) 
                { 
                    if (Guid.TryParse(input, out var guid)) 
                        return (T)(object)guid; 
                    throw new FormatException(); 
                } 
                return (T)Convert.ChangeType( 
                    input, 
                    typeof(T), 
                    CultureInfo.InvariantCulture); 
            } catch (FormatException) 
            { 
                ShowError($"Invalid {typeof(T).Name}."); 
            } catch (InvalidCastException) 
            { 
                ShowError($"Cannot convert input {input} to {typeof(T).Name}."); 
            } 
        }
    } 
}