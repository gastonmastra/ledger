using Ledger.BuildingBlocks.Application;
using Ledger.BuildingBlocks.Infrastructure;
using Ledger.BuildingBlocks.Infrastructure.Messaging;
using Ledger.Modules.Contacts.Contracts;
using Ledger.Modules.Debts.Contracts;
using Ledger.Modules.Groups.Application;
using Ledger.Modules.Identity.Contracts;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ledger.Modules.Groups.Infrastructure;

internal sealed class GroupsEventBus(GroupsDbContext dbContext)
    : ModuleEventBus<GroupsDbContext>(dbContext), IGroupsEventBus;

internal sealed class UserRegisteredConsumer(ISender sender, GroupsDbContext dbContext)
    : ModuleConsumer<UserRegistered, GroupsDbContext>(dbContext)
{
    protected override Task Handle(ConsumeContext<UserRegistered> context)
        => sender.Send(new ProjectUserCommand(context.Message.UserId, context.Message.DisplayName), context.CancellationToken);
}

internal sealed class ContactCreatedConsumer(ISender sender, GroupsDbContext dbContext)
    : ModuleConsumer<ContactCreated, GroupsDbContext>(dbContext)
{
    protected override Task Handle(ConsumeContext<ContactCreated> context)
    {
        var c = context.Message;
        return sender.Send(new ProjectContactCommand(c.ContactId, c.OwnerId, c.PersonId, c.Name, c.LinkedUserId), context.CancellationToken);
    }
}

internal sealed class ContactLinkedConsumer(ISender sender, GroupsDbContext dbContext)
    : ModuleConsumer<ContactLinked, GroupsDbContext>(dbContext)
{
    protected override Task Handle(ConsumeContext<ContactLinked> context)
    {
        var c = context.Message;
        return sender.Send(new ProjectContactLinkCommand(c.ContactId, c.PersonId, c.LinkedUserId), context.CancellationToken);
    }
}

internal sealed class PersonMergedConsumer(ISender sender, GroupsDbContext dbContext)
    : ModuleConsumer<PersonMerged, GroupsDbContext>(dbContext)
{
    protected override Task Handle(ConsumeContext<PersonMerged> context)
    {
        var merged = context.Message;
        var name = merged.Parties.FirstOrDefault(p => p.PersonId == merged.UserId)?.DisplayName ?? "(unknown)";

        return sender.Send(new MergeGroupMemberCommand(merged.PreviousPersonId, merged.UserId, name), context.CancellationToken);
    }
}

/// <summary>Members that took part in group expenses cannot be removed anymore.</summary>
internal sealed class DebtOperationCompletedConsumer(ISender sender, GroupsDbContext dbContext)
    : ModuleConsumer<DebtOperationCompleted, GroupsDbContext>(dbContext)
{
    protected override Task Handle(ConsumeContext<DebtOperationCompleted> context)
    {
        var message = context.Message;

        if (message.GroupId is not { } groupId)
            return Task.CompletedTask;

        return sender.Send(
            new MarkGroupActivityCommand(groupId, message.Parties.Select(p => p.PersonId).ToList()),
            context.CancellationToken);
    }
}

public static class GroupsModule
{
    public const string Name = "groups";

    public static IServiceCollection AddGroupsPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddModuleDbContext<GroupsDbContext>(configuration, GroupsDbContext.Schema);
        services.AddScoped<IGroupsDbContext>(sp => sp.GetRequiredService<GroupsDbContext>());

        return services;
    }

    public static IServiceCollection AddGroupsModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddGroupsPersistence(configuration);
        services.AddModuleApplication(typeof(IGroupsDbContext).Assembly);
        services.AddScoped<IGroupsEventBus, GroupsEventBus>();
        services.AddModuleMessaging<GroupsDbContext>(
            Name,
            typeof(UserRegisteredConsumer),
            typeof(ContactCreatedConsumer),
            typeof(ContactLinkedConsumer),
            typeof(PersonMergedConsumer),
            typeof(DebtOperationCompletedConsumer));

        return services;
    }
}
