using Ledger.Modules.Accounts.Domain;

namespace Ledger.Modules.Accounts.Application.CreateAccount;

public sealed record CreateAccountCommand(
    string Name,
    AccountType AccountType);