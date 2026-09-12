using Ledger.Modules.Identity.Domain;

namespace Ledger.Modules.Identity.Application.Users.GetOrCreateUser;

public sealed class GetOrCreateUserHandler(
    IUserRepository repository
)
{
    private readonly IUserRepository _repository = repository;

    public async Task<UserResult> Hanlde(
        GetOrCreateUserCommand command,
        CancellationToken cancellationToken)
    {
        var existingUser = await _repository.GetByExternalIdAsync(
            command.ExternalId,
            cancellationToken);

        if (existingUser is not null)
            return new UserResult(
                existingUser.Id,
                existingUser.ExternalId,
                existingUser.Email);

        var user = User.Create(
            command.ExternalId,
            command.Email);

        await _repository.AddAsync(user, cancellationToken);

        await _repository.SaveChangesAsync(cancellationToken);

        return new UserResult(
            user.Id,
            user.ExternalId,
            user.Email);
    }
}