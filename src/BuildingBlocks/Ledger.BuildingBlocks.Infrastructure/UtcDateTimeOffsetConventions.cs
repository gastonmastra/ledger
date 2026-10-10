using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Ledger.BuildingBlocks.Infrastructure;

/// <summary>
/// PostgreSQL <c>timestamp with time zone</c> only accepts UTC values, but clients may send dates
/// with any offset (e.g. "2026-09-01T10:00:00+02:00"). Every DateTimeOffset is normalized to UTC
/// when it is written or used in a query; the instant it represents does not change.
/// </summary>
public static class UtcDateTimeOffsetConventions
{
    public static ModelConfigurationBuilder UseUtcDateTimeOffsets(this ModelConfigurationBuilder builder)
    {
        builder.Properties<DateTimeOffset>().HaveConversion<UtcDateTimeOffsetConverter>();
        builder.Properties<DateTimeOffset?>().HaveConversion<UtcDateTimeOffsetConverter>();

        return builder;
    }

    private sealed class UtcDateTimeOffsetConverter() : ValueConverter<DateTimeOffset, DateTimeOffset>(
        value => value.ToUniversalTime(),
        value => value);
}
