namespace Ledger.Modules.Transactions.Application.GetTransaction;

public sealed record GetTransactionQuery(
    Guid TransactionId);