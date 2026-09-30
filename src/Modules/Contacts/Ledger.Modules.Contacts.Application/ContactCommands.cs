using FluentValidation;
using Ledger.BuildingBlocks.Application;
using Ledger.BuildingBlocks.Application.Exceptions;
using Ledger.BuildingBlocks.Domain;
using Ledger.Modules.Contacts.Contracts;
using Ledger.Modules.Contacts.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Modules.Contacts.Application;

public sealed record ContactDto(
    Guid Id,
    Guid PersonId,
    string Name,
    string? Email,
    Guid? LinkedUserId,
    bool IsLinked,
    ContactOrigin Origin,
    bool IsInUse);

/// <param name="Email">When it belongs to a registered user, the contact is linked to that user.</param>
public sealed record CreateContactCommand(Guid OwnerId, string Name, string? Email) : ICommand<ContactDto>;

public sealed record LinkContactCommand(Guid OwnerId, Guid ContactId, string Email) : ICommand<ContactDto>;

public sealed record GetContactsQuery(Guid OwnerId) : IQuery<IReadOnlyList<ContactDto>>;

public sealed record PersonReference(Guid PersonId, string Name, Guid? UserId);

/// <summary>
/// Internal: every registered user among <paramref name="People"/> gets a contact for each of the
/// other people (created if missing), and all those contacts are marked as in use.
/// </summary>
public sealed record EnsureMutualContactsCommand(IReadOnlyList<PersonReference> People) : ICommand;

public sealed record ProjectRegisteredUserCommand(Guid UserId, string Email, string DisplayName) : ICommand;

internal sealed class CreateContactValidator : AbstractValidator<CreateContactCommand>
{
    public CreateContactValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Contact.NameMaxLength);
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
    }
}

internal sealed class LinkContactValidator : AbstractValidator<LinkContactCommand>
{
    public LinkContactValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
    }
}

internal sealed class ContactHandlers(
    IContactsDbContext dbContext,
    IContactsEventBus eventBus)
    : ICommandHandler<CreateContactCommand, ContactDto>,
      ICommandHandler<LinkContactCommand, ContactDto>,
      ICommandHandler<EnsureMutualContactsCommand>,
      ICommandHandler<ProjectRegisteredUserCommand>,
      IQueryHandler<GetContactsQuery, IReadOnlyList<ContactDto>>
{
    public async Task<ContactDto> Handle(CreateContactCommand command, CancellationToken cancellationToken)
    {
        var user = await FindUserAsync(command.Email, cancellationToken);

        if (user is not null)
            await EnsureNotAlreadyAContactAsync(command.OwnerId, user.UserId, cancellationToken);

        var contact = Contact.Create(command.OwnerId, command.Name, command.Email, user?.UserId);

        dbContext.Contacts.Add(contact);
        await eventBus.PublishAsync(Created(contact), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return ToDto(contact);
    }

    public async Task<ContactDto> Handle(LinkContactCommand command, CancellationToken cancellationToken)
    {
        var contact = await dbContext.Contacts.SingleOrDefaultAsync(
            x => x.Id == command.ContactId && x.OwnerId == command.OwnerId,
            cancellationToken)
            ?? throw new NotFoundException("Contact", command.ContactId);

        var user = await FindUserAsync(command.Email, cancellationToken)
            ?? throw new DomainException(
                "contact.user_not_found",
                $"No registered user has the email '{command.Email}'. They must log in to Ledger once first.");

        await EnsureNotAlreadyAContactAsync(command.OwnerId, user.UserId, cancellationToken);

        var previousPersonId = contact.PersonId;
        contact.LinkTo(user.UserId, user.Email);

        await eventBus.PublishAsync(
            new ContactLinked(contact.Id, contact.OwnerId, previousPersonId, contact.PersonId, user.UserId, contact.Name),
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return ToDto(contact);
    }

    public async Task Handle(EnsureMutualContactsCommand command, CancellationToken cancellationToken)
    {
        var personIds = command.People.Select(p => p.PersonId).ToList();

        foreach (var owner in command.People.Where(p => p.UserId is not null))
        {
            var ownerId = owner.UserId!.Value;

            var existing = await dbContext.Contacts
                .Where(c => c.OwnerId == ownerId && personIds.Contains(c.PersonId))
                .ToDictionaryAsync(c => c.PersonId, cancellationToken);

            foreach (var other in command.People.Where(p => p.PersonId != owner.PersonId && p.UserId != ownerId))
            {
                if (existing.TryGetValue(other.PersonId, out var contact))
                {
                    contact.MarkInUse();
                    continue;
                }

                contact = Contact.CreateMirrored(ownerId, other.PersonId, other.Name, other.UserId);
                dbContext.Contacts.Add(contact);
                await eventBus.PublishAsync(Created(contact), cancellationToken);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task Handle(ProjectRegisteredUserCommand command, CancellationToken cancellationToken)
    {
        if (await dbContext.RegisteredUsers.AnyAsync(x => x.UserId == command.UserId, cancellationToken))
            return;

        dbContext.RegisteredUsers.Add(RegisteredUser.Create(command.UserId, command.Email, command.DisplayName));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ContactDto>> Handle(GetContactsQuery query, CancellationToken cancellationToken)
    {
        var contacts = await dbContext.Contacts
            .AsNoTracking()
            .Where(x => x.OwnerId == query.OwnerId)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        return contacts.Select(ToDto).ToList();
    }

    private async Task<RegisteredUser?> FindUserAsync(string? email, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email))
            return null;

        var normalized = email.Trim().ToLowerInvariant();

        return await dbContext.RegisteredUsers
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Email == normalized, cancellationToken);
    }

    private async Task EnsureNotAlreadyAContactAsync(Guid ownerId, Guid userId, CancellationToken cancellationToken)
    {
        if (await dbContext.Contacts.AnyAsync(x => x.OwnerId == ownerId && x.PersonId == userId, cancellationToken))
            throw new ConflictException("That user is already one of your contacts.");
    }

    private static ContactCreated Created(Contact contact)
        => new(contact.Id, contact.OwnerId, contact.PersonId, contact.Name, contact.Email, contact.LinkedUserId);

    private static ContactDto ToDto(Contact contact)
        => new(
            contact.Id,
            contact.PersonId,
            contact.Name,
            contact.Email,
            contact.LinkedUserId,
            contact.LinkedUserId is not null,
            contact.Origin,
            contact.IsInUse);
}
