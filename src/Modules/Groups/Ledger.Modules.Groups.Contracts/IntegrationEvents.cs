namespace Ledger.Modules.Groups.Contracts;

public sealed record GroupMemberInfo(
    Guid PersonId,
    string DisplayName,
    Guid? UserId);

public sealed record GroupCreated(
    Guid GroupId,
    string Name,
    Guid CreatedBy,
    IReadOnlyList<GroupMemberInfo> Members);

public sealed record GroupMemberAdded(
    Guid GroupId,
    string GroupName,
    GroupMemberInfo Member,
    IReadOnlyList<GroupMemberInfo> Members);

public sealed record GroupMemberRemoved(
    Guid GroupId,
    Guid PersonId);
