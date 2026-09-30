namespace Ledger.BuildingBlocks.Application;

/// <summary>The authenticated user, identified by the internal Identity user id.</summary>
public interface ICurrentUser
{
    Guid UserId { get; }
}
