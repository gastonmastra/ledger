using Ledger.Modules.Accounts.Domain;

namespace Ledger.Modules.Accounts.Application.GetAccount;

public sealed record AccountDto(
    Guid Id,
    string Name,
    AccountType Type, 
    bool IsArchived);