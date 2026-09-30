using Ledger.BuildingBlocks.Application;
using Ledger.BuildingBlocks.Application.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Modules.Identity.Application.Users.GetUser;

public sealed record GetUserQuery(Guid UserId) : IQuery<UserResult>;

internal sealed class GetUserHandler(IIdentityDbContext dbContext)
    : IQueryHandler<GetUserQuery, UserResult>
{
    public async Task<UserResult> Handle(GetUserQuery query, CancellationToken cancellationToken)
    {
        return await dbContext.Users
            .AsNoTracking()
            .Where(x => x.Id == query.UserId)
            .Select(x => new UserResult(x.Id, x.ExternalId, x.Email, x.DisplayName))
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("User", query.UserId);
    }
}
