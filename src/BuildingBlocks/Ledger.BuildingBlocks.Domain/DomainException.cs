namespace Ledger.BuildingBlocks.Domain;

/// <summary>
/// Raised when a business rule is violated. Hosts translate it into a 400 response.
/// </summary>
public class DomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
