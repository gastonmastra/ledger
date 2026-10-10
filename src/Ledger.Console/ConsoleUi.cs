using System.Globalization;
using SystemConsole = System.Console;

namespace Ledger.Console;

public record Option(int Order, string Description);

public static class ConsoleUi
{
    private const int Width = 50;

    public static Option ShowMenu(string title, Option[] options, string? subtitle = null)
    {
        Header(title);

        if (subtitle is not null)
            SystemConsole.WriteLine(subtitle + "\n");

        foreach (var option in options)
            SystemConsole.WriteLine($"{option.Order}. {option.Description}");

        SystemConsole.WriteLine();

        return ReadOption(options);
    }

    public static void Header(string title)
    {
        SystemConsole.Clear();
        SystemConsole.WriteLine(new string('=', Width));
        SystemConsole.WriteLine(CenterText(title, Width));
        SystemConsole.WriteLine(new string('=', Width));
        SystemConsole.WriteLine();
    }

    public static Option ReadOption(Option[] options, string prompt = "Choose an option: ")
    {
        while (true)
        {
            SystemConsole.Write(prompt);

            if (int.TryParse(ReadLine(), out var selected)
                && options.FirstOrDefault(x => x.Order == selected) is { } option)
                return option;

            Error("Invalid option.");
        }
    }

    public static string ReadText(string prompt, bool optional = false)
    {
        while (true)
        {
            SystemConsole.Write(prompt);
            var input = ReadLine().Trim();

            if (!string.IsNullOrEmpty(input))
                return input;

            if (optional)
                return "";

            Error("A value is required.");
        }
    }

    public static decimal ReadDecimal(string prompt)
        => ReadOptionalDecimal(prompt, optional: false)!.Value;

    public static decimal? ReadOptionalDecimal(string prompt, bool optional = true)
    {
        while (true)
        {
            var input = ReadText(prompt, optional);

            if (optional && input.Length == 0)
                return null;

            if (decimal.TryParse(input.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
                return value;

            Error("Enter a number, e.g. 12.50");
        }
    }

    public static DateTimeOffset? ReadOptionalDate(string prompt)
    {
        while (true)
        {
            var input = ReadText(prompt, optional: true);

            if (input.Length == 0)
                return null;

            if (DateTimeOffset.TryParse(input, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var date))
                return date;

            Error("Enter a date as yyyy-MM-dd.");
        }
    }

    /// <summary>
    /// Asks for a local date and time ("yyyy-MM-dd HH:mm" or "yyyy-MM-dd"); ENTER = now.
    /// Returns it in UTC; future dates are not accepted.
    /// </summary>
    public static DateTimeOffset ReadWhen(string prompt = "When (yyyy-MM-dd HH:mm, ENTER = now): ", DateTimeOffset? defaultValue = null)
    {
        while (true)
        {
            var input = ReadText(prompt, optional: true);

            if (input.Length == 0)
                return (defaultValue ?? DateTimeOffset.Now).ToUniversalTime();

            string[] formats = ["yyyy-MM-dd HH:mm", "yyyy-MM-dd H:mm", "yyyy-MM-dd"];

            if (DateTime.TryParseExact(input, formats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var local))
            {
                var when = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local)).ToUniversalTime();

                if (when <= DateTimeOffset.UtcNow.AddMinutes(1))
                    return when;

                Error("The date cannot be in the future.");
                continue;
            }

            Error("Use the format yyyy-MM-dd HH:mm (e.g. 2026-09-30 18:45).");
        }
    }

    /// <summary>Why something is edited, deleted or restored: required and kept in the history.</summary>
    public static string ReadReason(string prompt = "Why? (kept in the history): ")
    {
        while (true)
        {
            var reason = ReadText(prompt);

            if (reason.Length <= 500)
                return reason;

            Error("Use at most 500 characters.");
        }
    }

    /// <summary>Text with a current value: ENTER keeps it, "-" clears it.</summary>
    public static string? ReadTextOrKeep(string label, string? current)
    {
        var input = ReadText($"{label} [{current ?? ""}] (ENTER = keep, - = clear): ", optional: true);
        return input.Length == 0 ? current : input == "-" ? null : input;
    }

    /// <summary>Amount with a current value: ENTER keeps it.</summary>
    public static decimal ReadDecimalOrKeep(string label, decimal current)
        => ReadOptionalDecimal($"{label} [{Amount(current)}] (ENTER = keep): ") ?? current;

    public static bool Confirm(string prompt)
        => ReadText($"{prompt} (y/n): ").Equals("y", StringComparison.OrdinalIgnoreCase);

    /// <summary>Lets the user pick one item of a list by its number.</summary>
    public static T? Choose<T>(string title, IReadOnlyList<T> items, Func<T, string> label, bool allowNone = false)
        where T : class
    {
        if (items.Count == 0)
        {
            Error($"There is nothing to choose ({title.ToLowerInvariant()}).");
            return null;
        }

        SystemConsole.WriteLine($"\n{title}:");

        for (var i = 0; i < items.Count; i++)
            SystemConsole.WriteLine($"  {i + 1}. {label(items[i])}");

        if (allowNone)
            SystemConsole.WriteLine("  0. None");

        while (true)
        {
            SystemConsole.Write("Number: ");

            if (int.TryParse(ReadLine(), out var number))
            {
                if (allowNone && number == 0)
                    return null;

                if (number >= 1 && number <= items.Count)
                    return items[number - 1];
            }

            Error("Invalid number.");
        }
    }

    public static void Table<T>(IEnumerable<T> items, params (string Header, Func<T, object?> Value)[] columns)
    {
        var rows = items.ToList();

        if (rows.Count == 0)
        {
            SystemConsole.WriteLine("(nothing yet)");
            return;
        }

        var widths = columns
            .Select(column => Math.Max(column.Header.Length, rows.Max(r => Format(column.Value(r)).Length)))
            .ToArray();

        SystemConsole.WriteLine(string.Join("  ", columns.Select((c, i) => c.Header.PadRight(widths[i]))));
        SystemConsole.WriteLine(string.Join("  ", widths.Select(w => new string('-', w))));

        foreach (var row in rows)
            SystemConsole.WriteLine(string.Join("  ", columns.Select((c, i) => Format(c.Value(row)).PadRight(widths[i]))));
    }

    public static string Money(decimal amount, string currency)
        => $"{Amount(amount)} {currency}";

    public static string Amount(decimal amount)
        => amount.ToString("N2", CultureInfo.InvariantCulture);

    public static void Success(string message)
    {
        SystemConsole.ForegroundColor = ConsoleColor.Green;
        SystemConsole.WriteLine($"\n{message}");
        SystemConsole.ResetColor();
        Pause();
    }

    public static void Error(string error)
    {
        SystemConsole.ForegroundColor = ConsoleColor.Red;
        SystemConsole.WriteLine($"[ERROR] {error}");
        SystemConsole.ResetColor();
    }

    public static void Pause()
    {
        SystemConsole.Write("\nPress ENTER to continue...");
        ReadLine();
    }

    /// <summary>Reads a line; the end of the input (Ctrl+D, closed pipe) ends the application.</summary>
    public static string ReadLine()
        => SystemConsole.ReadLine() ?? throw new OperationCanceledException("The input was closed.");

    private static string Format(object? value)
        => value switch
        {
            null => "",
            decimal d => d.ToString("N2", CultureInfo.InvariantCulture),
            DateTimeOffset date => date.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            _ => value.ToString() ?? ""
        };

    private static string CenterText(string text, int width)
        => text.Length >= width ? text : text.PadLeft((width - text.Length) / 2 + text.Length).PadRight(width);
}
