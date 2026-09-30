using System.Globalization;
using Ledger.Console.Api;
using SystemConsole = System.Console;

namespace Ledger.Console.Menus;

/// <summary>Text version of the dashboard charts: income vs expenses, spending, net worth and wallets.</summary>
public sealed class ReportsMenu(LedgerApiClient api) : Menu
{
    private const int BarWidth = 30;

    protected override string Title => "REPORTS";

    protected override IReadOnlyList<(string, Func<CancellationToken, Task>)> Actions =>
    [
        ("This month", ct => ShowAsync(MonthRange(DateTime.Now), ct)),
        ("This year", ct => ShowAsync(YearRange(DateTime.Now), ct)),
        ("Another month", ct => ShowAsync(MonthRange(ReadMonth()), ct)),
        ("Another year", ct => ShowAsync(YearRange(new DateTime(ReadYear(), 1, 1)), ct)),
        ("Custom range", ct => ShowAsync(CustomRange(), ct))
    ];

    private async Task ShowAsync((DateTimeOffset From, DateTimeOffset To, string Title) period, CancellationToken ct)
    {
        var report = await api.ReportAsync(period.From, period.To, null, ct);

        if (report.Currencies.Count > 1)
        {
            var currency = ConsoleUi.Choose($"Currency (default {report.Currency})", report.Currencies, c => c)!;
            if (currency != report.Currency)
                report = await api.ReportAsync(period.From, period.To, currency, ct);
        }

        ConsoleUi.Header($"REPORT · {period.Title} · {report.Currency}");

        var result = report.TotalIncome - report.TotalExpenses;
        SystemConsole.WriteLine($"Income {Money(report.TotalIncome, report)}   Expenses {Money(report.TotalExpenses, report)}   Result {Money(result, report)}");

        Section("Income vs expenses");
        var active = report.Buckets.Where(b => b.Income != 0 || b.Expenses != 0).ToList();
        var maxFlow = active.Select(b => Math.Max(b.Income, b.Expenses)).DefaultIfEmpty(0).Max();

        if (active.Count == 0)
            SystemConsole.WriteLine("(no income or expenses in this period)");

        foreach (var bucket in active)
        {
            SystemConsole.WriteLine($"{Label(bucket.Start, report),-10} + {Bar(bucket.Income, maxFlow),-BarWidth} {Money(bucket.Income, report)}");
            SystemConsole.WriteLine($"{"",-10} - {Bar(bucket.Expenses, maxFlow),-BarWidth} {Money(bucket.Expenses, report)}");
        }

        Section("Spending by category");
        var maxSpending = report.SpendingByCategory.Select(c => c.Amount).DefaultIfEmpty(0).Max();

        if (report.SpendingByCategory.Count == 0)
            SystemConsole.WriteLine("(no expenses in this period)");

        foreach (var category in report.SpendingByCategory)
            SystemConsole.WriteLine($"{Truncate(category.Name, 20),-20} {Bar(category.Amount, maxSpending),-BarWidth} {Money(category.Amount, report)}");

        Section("Net worth");
        // Only the buckets where it changed (plus the last one), to keep it short.
        var previous = (decimal?)null;
        var lines = report.Buckets
            .Where((b, i) =>
            {
                var changed = b.NetWorth != previous || i == report.Buckets.Count - 1;
                previous = b.NetWorth;
                return changed;
            })
            .ToList();
        var maxWorth = lines.Select(b => Math.Abs(b.NetWorth)).DefaultIfEmpty(0).Max();

        foreach (var bucket in lines)
            SystemConsole.WriteLine($"{Label(bucket.Start, report),-10} {Bar(Math.Abs(bucket.NetWorth), maxWorth),-BarWidth} {Money(bucket.NetWorth, report)}");

        Section("Balance per wallet (end of the period)");
        var maxWallet = report.WalletBalances.Select(w => Math.Abs(w.Balance)).DefaultIfEmpty(0).Max();

        foreach (var wallet in report.WalletBalances)
            SystemConsole.WriteLine($"{Truncate(wallet.Name, 20),-20} {Bar(Math.Abs(wallet.Balance), maxWallet),-BarWidth} {Money(wallet.Balance, report)}");

        ConsoleUi.Pause();
    }

    private static (DateTimeOffset, DateTimeOffset, string) MonthRange(DateTime anyDay)
    {
        var start = new DateTime(anyDay.Year, anyDay.Month, 1);
        return (Local(start), Local(start.AddMonths(1).AddTicks(-1)), start.ToString("MMMM yyyy", CultureInfo.InvariantCulture));
    }

    private static (DateTimeOffset, DateTimeOffset, string) YearRange(DateTime anyDay)
    {
        var start = new DateTime(anyDay.Year, 1, 1);
        return (Local(start), Local(start.AddYears(1).AddTicks(-1)), start.Year.ToString(CultureInfo.InvariantCulture));
    }

    private static (DateTimeOffset, DateTimeOffset, string) CustomRange()
    {
        var from = ReadDate("From (yyyy-MM-dd): ");
        var to = ReadDate("To (yyyy-MM-dd): ");

        if (to < from)
            (from, to) = (to, from);

        return (Local(from), Local(to.AddDays(1).AddTicks(-1)), $"{from:yyyy-MM-dd} – {to:yyyy-MM-dd}");
    }

    private static DateTime ReadMonth()
    {
        while (true)
        {
            var input = ConsoleUi.ReadText("Month (yyyy-MM): ");
            if (DateTime.TryParseExact(input, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month))
                return month;

            ConsoleUi.Error("Use the format yyyy-MM, e.g. 2026-08.");
        }
    }

    private static int ReadYear()
    {
        while (true)
        {
            if (int.TryParse(ConsoleUi.ReadText("Year (yyyy): "), out var year) && year is >= 2000 and <= 2100)
                return year;

            ConsoleUi.Error("Enter a year, e.g. 2025.");
        }
    }

    private static DateTime ReadDate(string prompt)
    {
        while (true)
        {
            if (DateTime.TryParseExact(ConsoleUi.ReadText(prompt), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                return date;

            ConsoleUi.Error("Use the format yyyy-MM-dd.");
        }
    }

    private static DateTimeOffset Local(DateTime local) => new(local, TimeZoneInfo.Local.GetUtcOffset(local));

    private static string Label(DateTimeOffset start, Report report)
        => start.ToLocalTime().ToString(report.Granularity == "Day" ? "dd MMM" : "MMM yyyy", CultureInfo.InvariantCulture);

    private static string Bar(decimal value, decimal max)
        => max <= 0 || value <= 0 ? "" : new string('█', Math.Max(1, (int)Math.Round(value / max * BarWidth)));

    private static string Money(decimal amount, Report report) => ConsoleUi.Money(amount, report.Currency);

    private static string Truncate(string text, int length) => text.Length <= length ? text : text[..(length - 1)] + "…";

    private static void Section(string title)
    {
        SystemConsole.WriteLine();
        SystemConsole.WriteLine(title);
        SystemConsole.WriteLine(new string('-', title.Length));
    }
}
