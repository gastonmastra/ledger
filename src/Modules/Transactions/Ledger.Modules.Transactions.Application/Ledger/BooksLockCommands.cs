using FluentValidation;
using Ledger.BuildingBlocks.Application;
using Ledger.Modules.Transactions.Contracts;
using Ledger.Modules.Transactions.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Modules.Transactions.Application.Ledger;

public sealed record BooksLockDto(DateTimeOffset? LockedUntil);

public sealed record GetBooksLockQuery(Guid UserId) : IQuery<BooksLockDto>;

/// <summary>Closes the books up to a date (null reopens everything).</summary>
public sealed record SetBooksLockCommand(Guid UserId, DateTimeOffset? LockedUntil) : ICommand<BooksLockDto>;

internal sealed class SetBooksLockValidator : AbstractValidator<SetBooksLockCommand>
{
    public SetBooksLockValidator()
    {
        RuleFor(x => x.LockedUntil).NotInFuture();
    }
}

internal sealed class BooksLockHandlers(
    ITransactionsDbContext dbContext,
    ITransactionsEventBus eventBus)
    : IQueryHandler<GetBooksLockQuery, BooksLockDto>,
      ICommandHandler<SetBooksLockCommand, BooksLockDto>
{
    public async Task<BooksLockDto> Handle(GetBooksLockQuery query, CancellationToken cancellationToken)
    {
        var booksLock = await dbContext.BooksLocks.AsNoTracking()
            .SingleOrDefaultAsync(l => l.OwnerId == query.UserId, cancellationToken);

        return new BooksLockDto(booksLock?.LockedUntil);
    }

    public async Task<BooksLockDto> Handle(SetBooksLockCommand command, CancellationToken cancellationToken)
    {
        var booksLock = await dbContext.BooksLocks.SingleOrDefaultAsync(l => l.OwnerId == command.UserId, cancellationToken);

        if (booksLock is null)
        {
            booksLock = BooksLock.For(command.UserId);
            dbContext.BooksLocks.Add(booksLock);
        }

        booksLock.Set(command.LockedUntil);

        // Other modules (Debts) need it to reject shared operations that would touch a locked period.
        await eventBus.PublishAsync(new BooksLockChanged(command.UserId, command.LockedUntil), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new BooksLockDto(booksLock.LockedUntil);
    }
}
