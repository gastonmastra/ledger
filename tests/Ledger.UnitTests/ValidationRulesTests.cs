using FluentValidation;
using Ledger.BuildingBlocks.Application;

namespace Ledger.UnitTests;

public class ValidationRulesTests
{
    private sealed record Dated(DateTimeOffset When, DateTimeOffset? Optional);

    private sealed class DatedValidator : AbstractValidator<Dated>
    {
        public DatedValidator()
        {
            RuleFor(x => x.When).NotInFuture();
            RuleFor(x => x.Optional).NotInFuture();
        }
    }

    [Fact]
    public void Dates_cannot_be_in_the_future()
    {
        var validator = new DatedValidator();

        Assert.True(validator.Validate(new Dated(DateTimeOffset.UtcNow.AddDays(-3), null)).IsValid);
        Assert.True(validator.Validate(new Dated(DateTimeOffset.UtcNow.AddSeconds(30), null)).IsValid); // clock skew
        Assert.False(validator.Validate(new Dated(DateTimeOffset.UtcNow.AddHours(1), null)).IsValid);
        Assert.False(validator.Validate(new Dated(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1))).IsValid);
    }
}
