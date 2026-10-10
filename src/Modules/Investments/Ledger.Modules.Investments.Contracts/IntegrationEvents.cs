namespace Ledger.Modules.Investments.Contracts;

/// <summary>A user added an asset (share, ETF, crypto…) to their catalogue. <c>Currency</c> is its quote currency.</summary>
public sealed record AssetCreated(Guid AssetId, Guid OwnerId, string Name, string Currency);

public sealed record AssetRenamed(Guid AssetId, Guid OwnerId, string Name);

public sealed record AssetArchived(Guid AssetId, Guid OwnerId);
