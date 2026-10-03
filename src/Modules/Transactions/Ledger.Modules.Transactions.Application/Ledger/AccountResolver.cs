using Ledger.BuildingBlocks.Application.Exceptions;
using Ledger.BuildingBlocks.Domain;
using Ledger.Modules.Transactions.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Modules.Transactions.Application.Ledger;

/// <summary>Finds the ledger accounts behind wallets, categories and system purposes.</summary>
internal sealed class AccountResolver(ITransactionsDbContext dbContext)
{
    public async Task<LedgerAccount> WalletAsync(Guid userId, Guid walletId, CancellationToken cancellationToken)
    {
        var account = await dbContext.LedgerAccounts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Purpose == LedgerAccountPurpose.Wallet && x.ReferenceId == walletId,
                cancellationToken);

        if (account is null || account.OwnerId != userId)
            throw new NotFoundException("Wallet", walletId);

        if (account.IsArchived)
            throw new DomainException("ledger.wallet_archived", $"Wallet '{account.Name}' is archived.");

        return account;
    }

    /// <remarks>
    /// The account of a new asset is created asynchronously, so its absence is transient: a
    /// <see cref="NotYetAvailableException"/> makes the consumer retry.
    /// </remarks>
    public async Task<LedgerAccount> InvestmentAsync(Guid userId, Guid assetId, CancellationToken cancellationToken)
    {
        var account = await dbContext.LedgerAccounts
                          .AsNoTracking()
                          .SingleOrDefaultAsync(
                              x => x.Purpose == LedgerAccountPurpose.Investment && x.ReferenceId == assetId,
                              cancellationToken)
                      ?? throw new NotYetAvailableException($"The account of asset '{assetId}' is not known yet.");

        if (account.OwnerId != userId)
            throw new NotFoundException("Asset", assetId);

        return account;
    }

    public async Task<LedgerAccount> CategoryAsync(
        Guid userId,
        Guid? categoryId,
        LedgerAccountType type,
        CancellationToken cancellationToken)
    {
        if (categoryId is null)
        {
            return await SystemAsync(
                userId,
                type == LedgerAccountType.Income
                    ? LedgerAccountPurpose.UncategorizedIncome
                    : LedgerAccountPurpose.Uncategorized,
                cancellationToken);
        }

        var account = await dbContext.LedgerAccounts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Id == new AccountId(categoryId.Value) && x.Purpose == LedgerAccountPurpose.Category,
                cancellationToken);

        if (account is null || account.OwnerId != userId)
            throw new NotFoundException("Category", categoryId.Value);

        if (account.Type != type)
            throw new DomainException(
                "ledger.wrong_category_type",
                $"Category '{account.Name}' is not an {type} category.");

        if (account.IsArchived)
            throw new DomainException("ledger.category_archived", $"Category '{account.Name}' is archived.");

        return account;
    }

    /// <remarks>
    /// System accounts are created asynchronously after the user registers, so their absence is
    /// transient: a <see cref="NotYetAvailableException"/> makes message consumers retry.
    /// </remarks>
    public async Task<LedgerAccount> SystemAsync(
        Guid userId,
        LedgerAccountPurpose purpose,
        CancellationToken cancellationToken)
        => await dbContext.LedgerAccounts
               .AsNoTracking()
               .SingleOrDefaultAsync(x => x.OwnerId == userId && x.Purpose == purpose, cancellationToken)
           ?? throw new NotYetAvailableException(
               $"The {purpose} account of user '{userId}' is not provisioned yet. Try again in a moment.");
}
