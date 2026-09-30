using Ledger.BuildingBlocks.Application;
using Ledger.Modules.Identity.Contracts;
using Ledger.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Modules.Identity.Application.Users.GetOrCreateUser;

internal sealed class GetOrCreateUserHandler(
    IIdentityDbContext dbContext,
    IIdentityEventBus eventBus)
    : ICommandHandler<GetOrCreateUserCommand, UserResult>
{
    public async Task<UserResult> Handle(
        GetOrCreateUserCommand command,
        CancellationToken cancellationToken)
    {
        var existingUser = await FindAsync(command.ExternalId, cancellationToken);

        if (existingUser is not null)
            return ToResult(existingUser);

        var user = User.Create(
            command.ExternalId,
            command.Email,
            command.DisplayName);

        dbContext.Users.Add(user);

        await eventBus.PublishAsync(
            new UserRegistered(user.Id, user.Email, user.DisplayName),
            cancellationToken);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Two first requests of the same user raced; the other one won.
            var winner = await FindAsync(command.ExternalId, cancellationToken);

            if (winner is null)
                throw;

            return ToResult(winner);
        }

        return ToResult(user);
    }

    private Task<User?> FindAsync(string externalId, CancellationToken cancellationToken)
        => dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.ExternalId == externalId, cancellationToken);

    private static UserResult ToResult(User user)
        => new(user.Id, user.ExternalId, user.Email, user.DisplayName);
}
