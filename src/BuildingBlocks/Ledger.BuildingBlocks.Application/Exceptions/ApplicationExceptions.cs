namespace Ledger.BuildingBlocks.Application.Exceptions;

public sealed class NotFoundException(string resource, object id)
    : Exception($"{resource} '{id}' was not found.");

public sealed class ForbiddenException(string message)
    : Exception(message);

public sealed class ConflictException(string message)
    : Exception(message);

/// <summary>
/// Something the request depends on is still being provisioned by another module (eventual
/// consistency). Hosts answer 503 so clients retry; message consumers retry automatically.
/// </summary>
public sealed class NotYetAvailableException(string message)
    : Exception(message);
