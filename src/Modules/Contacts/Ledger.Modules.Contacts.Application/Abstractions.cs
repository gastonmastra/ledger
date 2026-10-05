using Ledger.BuildingBlocks.Application;
using Ledger.Modules.Contacts.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Modules.Contacts.Application;

public interface IContactsDbContext
{
    DbSet<Contact> Contacts { get; }
    DbSet<RegisteredUser> RegisteredUsers { get; }
    DbSet<ContactLinkRequest> LinkRequests { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IContactsEventBus : IEventBus;
