using FluentValidation;

namespace Ledger.BuildingBlocks.Application;

public static class ValidationRules
{
    /// <summary>Tolerance for small clock differences between the client and the server.</summary>
    public static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(1);

    /// <summary>The date cannot be later than now.</summary>
    public static IRuleBuilderOptions<T, DateTimeOffset> NotInFuture<T>(this IRuleBuilder<T, DateTimeOffset> rule)
        => rule
            .Must(date => date <= DateTimeOffset.UtcNow.Add(ClockSkew))
            .WithErrorCode("date.in_future")
            .WithMessage("The date cannot be in the future.");

    /// <inheritdoc cref="NotInFuture{T}(IRuleBuilder{T, DateTimeOffset})"/>
    public static IRuleBuilderOptions<T, DateTimeOffset?> NotInFuture<T>(this IRuleBuilder<T, DateTimeOffset?> rule)
        => rule
            .Must(date => date is null || date <= DateTimeOffset.UtcNow.Add(ClockSkew))
            .WithErrorCode("date.in_future")
            .WithMessage("The date cannot be in the future.");
}
