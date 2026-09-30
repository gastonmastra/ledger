using Ledger.BuildingBlocks.Application;
using Ledger.BuildingBlocks.Infrastructure;
using Ledger.BuildingBlocks.Infrastructure.Messaging;
using Ledger.Modules.Contacts.Application;
using Ledger.Modules.Contacts.Infrastructure.Persistence;
using Ledger.Modules.Debts.Contracts;
using Ledger.Modules.Groups.Contracts;
using Ledger.Modules.Identity.Contracts;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ledger.Modules.Contacts.Infrastructure;

internal sealed class ContactsEventBus(ContactsDbContext dbContext)
    : ModuleEventBus<ContactsDbContext>(dbContext), IContactsEventBus;

internal sealed class UserRegisteredConsumer(ISender sender, ContactsDbContext dbContext)
    : ModuleConsumer<UserRegistered, ContactsDbContext>(dbContext)
{
    protected override Task Handle(ConsumeContext<UserRegistered> context)
        => sender.Send(
            new ProjectRegisteredUserCommand(context.Message.UserId, context.Message.Email, context.Message.DisplayName),
            context.CancellationToken);
}

/// <summary>People who share a debt see each other in their contacts.</summary>
internal sealed class DebtOperationCompletedConsumer(ISender sender, ContactsDbContext dbContext)
    : ModuleConsumer<DebtOperationCompleted, ContactsDbContext>(dbContext)
{
    protected override Task Handle(ConsumeContext<DebtOperationCompleted> context)
        => sender.Send(
            new EnsureMutualContactsCommand(context.Message.Parties
                .Select(p => new PersonReference(p.PersonId, p.DisplayName, p.UserId))
                .ToList()),
            context.CancellationToken);
}

/// <summary>Members of a shared group see each other in their contacts.</summary>
internal sealed class GroupCreatedConsumer(ISender sender, ContactsDbContext dbContext)
    : ModuleConsumer<GroupCreated, ContactsDbContext>(dbContext)
{
    protected override Task Handle(ConsumeContext<GroupCreated> context)
        => sender.Send(
            new EnsureMutualContactsCommand(context.Message.Members
                .Select(m => new PersonReference(m.PersonId, m.DisplayName, m.UserId))
                .ToList()),
            context.CancellationToken);
}

internal sealed class GroupMemberAddedConsumer(ISender sender, ContactsDbContext dbContext)
    : ModuleConsumer<GroupMemberAdded, ContactsDbContext>(dbContext)
{
    protected override Task Handle(ConsumeContext<GroupMemberAdded> context)
        => sender.Send(
            new EnsureMutualContactsCommand(context.Message.Members
                .Select(m => new PersonReference(m.PersonId, m.DisplayName, m.UserId))
                .ToList()),
            context.CancellationToken);
}

public static class ContactsModule
{
    public const string Name = "contacts";

    public static IServiceCollection AddContactsPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddModuleDbContext<ContactsDbContext>(configuration, ContactsDbContext.Schema);
        services.AddScoped<IContactsDbContext>(sp => sp.GetRequiredService<ContactsDbContext>());

        return services;
    }

    public static IServiceCollection AddContactsModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddContactsPersistence(configuration);
        services.AddModuleApplication(typeof(IContactsDbContext).Assembly);
        services.AddScoped<IContactsEventBus, ContactsEventBus>();
        services.AddModuleMessaging<ContactsDbContext>(
            Name,
            typeof(UserRegisteredConsumer),
            typeof(DebtOperationCompletedConsumer),
            typeof(GroupCreatedConsumer),
            typeof(GroupMemberAddedConsumer));

        return services;
    }
}
