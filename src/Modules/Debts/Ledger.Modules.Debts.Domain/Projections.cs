using Ledger.BuildingBlocks.Domain;

namespace Ledger.Modules.Debts.Domain;

/// <summary>Local copy of a contact of some user.</summary>
public sealed class DebtContact
{
    private DebtContact()
    {
        Name = null!;
    }

    public Guid ContactId { get; private set; }
    public Guid OwnerId { get; private set; }
    public Guid PersonId { get; private set; }
    public string Name { get; private set; }
    public Guid? LinkedUserId { get; private set; }

    public static DebtContact Create(Guid contactId, Guid ownerId, Guid personId, string name, Guid? linkedUserId)
        => new() { ContactId = contactId, OwnerId = ownerId, PersonId = personId, Name = name, LinkedUserId = linkedUserId };

    public void Link(Guid personId, Guid userId)
    {
        PersonId = personId;
        LinkedUserId = userId;
    }
}

/// <summary>Local copy of the registered users.</summary>
public sealed class DebtUser
{
    private DebtUser()
    {
        DisplayName = null!;
    }

    public Guid UserId { get; private set; }
    public string DisplayName { get; private set; }

    public static DebtUser Create(Guid userId, string displayName) => new() { UserId = userId, DisplayName = displayName };
}

/// <summary>Local copy of a group member.</summary>
public sealed class DebtGroupMember
{
    private DebtGroupMember()
    {
        DisplayName = null!;
        GroupName = null!;
    }

    public Guid GroupId { get; private set; }
    public string GroupName { get; private set; }
    public Guid PersonId { get; private set; }
    public string DisplayName { get; private set; }
    public Guid? UserId { get; private set; }

    public static DebtGroupMember Create(Guid groupId, string groupName, Guid personId, string displayName, Guid? userId)
        => new() { GroupId = groupId, GroupName = groupName, PersonId = personId, DisplayName = displayName, UserId = userId };
}

/// <summary>Local copy of a wallet (to validate currency and ownership).</summary>
public sealed class DebtWallet
{
    private DebtWallet()
    {
    }

    public Guid WalletId { get; private set; }
    public Guid UserId { get; private set; }
    public Currency Currency { get; private set; }

    public static DebtWallet Create(Guid walletId, Guid userId, Currency currency)
        => new() { WalletId = walletId, UserId = userId, Currency = currency };
}
