namespace Ledger.BuildingBlocks.Application;

public enum ReportGranularity
{
    Day,
    Month
}

/// <summary>
/// How reports cut a period into buckets (days or months in the user's time zone). Shared by every
/// module that produces chart series, so series of different modules line up bucket by bucket.
/// </summary>
public static class ReportBuckets
{
    /// <summary>Up to this many days a report shows days; above, months.</summary>
    public const int MaxDailyDays = 62;

    public static ReportGranularity GranularityFor(DateTimeOffset from, DateTimeOffset to)
        => (to - from).TotalDays <= MaxDailyDays ? ReportGranularity.Day : ReportGranularity.Month;

    /// <summary>Start of every day or month of the period, in the user's time zone. The first one is <paramref name="from"/>.</summary>
    public static List<DateTimeOffset> Starts(
        DateTimeOffset from,
        DateTimeOffset to,
        ReportGranularity granularity,
        TimeZoneInfo zone)
    {
        var starts = new List<DateTimeOffset> { from };
        var local = TimeZoneInfo.ConvertTime(from, zone).DateTime;
        var cursor = granularity == ReportGranularity.Day
            ? local.Date.AddDays(1)
            : new DateTime(local.Year, local.Month, 1).AddMonths(1);

        while (true)
        {
            var start = new DateTimeOffset(cursor, zone.GetUtcOffset(cursor));
            if (start > to) break;

            starts.Add(start);
            cursor = granularity == ReportGranularity.Day ? cursor.AddDays(1) : cursor.AddMonths(1);
        }

        return starts;
    }

    /// <summary>End (exclusive) of the bucket at <paramref name="index"/>.</summary>
    public static DateTimeOffset End(IReadOnlyList<DateTimeOffset> starts, int index, DateTimeOffset to)
        => index + 1 < starts.Count ? starts[index + 1] : to.AddTicks(1);

    /// <summary>The IANA (or Windows) time zone with that id; UTC when it is empty or unknown.</summary>
    public static TimeZoneInfo FindZone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return TimeZoneInfo.Utc;

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }
}
