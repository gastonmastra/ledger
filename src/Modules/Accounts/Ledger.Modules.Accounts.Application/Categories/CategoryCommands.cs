using FluentValidation;
using Ledger.BuildingBlocks.Application;
using Ledger.BuildingBlocks.Application.Exceptions;
using Ledger.Modules.Accounts.Contracts;
using Ledger.Modules.Accounts.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Modules.Accounts.Application.Categories;

public sealed record CategoryDto(Guid Id, string Name, AccountType Type, bool IsArchived);

public sealed record CreateCategoryCommand(Guid OwnerId, string Name, AccountType Type) : ICommand<CategoryDto>;

public sealed record RenameCategoryCommand(Guid OwnerId, Guid CategoryId, string Name) : ICommand;

public sealed record ArchiveCategoryCommand(Guid OwnerId, Guid CategoryId) : ICommand;

public sealed record GetCategoriesQuery(Guid OwnerId, bool IncludeArchived) : IQuery<IReadOnlyList<CategoryDto>>;

internal sealed class CreateCategoryValidator : AbstractValidator<CreateCategoryCommand>
{
    public CreateCategoryValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Account.NameMaxLength);
        RuleFor(x => x.Type)
            .Must(t => t is AccountType.Income or AccountType.Expense)
            .WithMessage("A category must be Income or Expense.");
    }
}

internal sealed class RenameCategoryValidator : AbstractValidator<RenameCategoryCommand>
{
    public RenameCategoryValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Account.NameMaxLength);
    }
}

internal sealed class CategoryHandlers(
    IAccountsDbContext dbContext,
    IAccountsEventBus eventBus)
    : ICommandHandler<CreateCategoryCommand, CategoryDto>,
      ICommandHandler<RenameCategoryCommand>,
      ICommandHandler<ArchiveCategoryCommand>,
      IQueryHandler<GetCategoriesQuery, IReadOnlyList<CategoryDto>>
{
    public async Task<CategoryDto> Handle(CreateCategoryCommand command, CancellationToken cancellationToken)
    {
        await EnsureUniqueNameAsync(command.OwnerId, command.Type, command.Name, null, cancellationToken);

        var category = Account.CreateCategory(command.OwnerId, command.Name, command.Type);

        dbContext.Accounts.Add(category);
        await eventBus.PublishAsync(AccountEvents.Created(category), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return ToDto(category);
    }

    public async Task Handle(RenameCategoryCommand command, CancellationToken cancellationToken)
    {
        var category = await GetOwnedCategoryAsync(command.OwnerId, command.CategoryId, cancellationToken);

        await EnsureUniqueNameAsync(command.OwnerId, category.Type, command.Name, category.Id, cancellationToken);

        category.Rename(command.Name);

        await eventBus.PublishAsync(
            new AccountRenamed(category.Id, category.OwnerId, category.Name),
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task Handle(ArchiveCategoryCommand command, CancellationToken cancellationToken)
    {
        var category = await GetOwnedCategoryAsync(command.OwnerId, command.CategoryId, cancellationToken);

        if (category.IsArchived)
            return;

        category.Archive();

        await eventBus.PublishAsync(new AccountArchived(category.Id, category.OwnerId), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CategoryDto>> Handle(GetCategoriesQuery query, CancellationToken cancellationToken)
    {
        return await dbContext.Accounts
            .AsNoTracking()
            .Where(x => x.OwnerId == query.OwnerId
                        && x.Purpose == AccountPurpose.Category
                        && (query.IncludeArchived || !x.IsArchived))
            .OrderBy(x => x.Type)
            .ThenBy(x => x.Name)
            .Select(x => new CategoryDto(x.Id, x.Name, x.Type, x.IsArchived))
            .ToListAsync(cancellationToken);
    }

    private async Task<Account> GetOwnedCategoryAsync(Guid ownerId, Guid categoryId, CancellationToken cancellationToken)
    {
        var category = await dbContext.Accounts.SingleOrDefaultAsync(
            x => x.Id == categoryId && x.Purpose == AccountPurpose.Category,
            cancellationToken);

        if (category is null || category.OwnerId != ownerId)
            throw new NotFoundException("Category", categoryId);

        return category;
    }

    private async Task EnsureUniqueNameAsync(
        Guid ownerId,
        AccountType type,
        string name,
        Guid? exceptId,
        CancellationToken cancellationToken)
    {
        var normalized = name.Trim().ToLower();

        var exists = await dbContext.Accounts.AnyAsync(
            x => x.OwnerId == ownerId
                 && x.Purpose == AccountPurpose.Category
                 && x.Type == type
                 && !x.IsArchived
                 && x.Id != exceptId
                 && x.Name.ToLower() == normalized,
            cancellationToken);

        if (exists)
            throw new ConflictException($"A {type} category named '{name.Trim()}' already exists.");
    }

    private static CategoryDto ToDto(Account account)
        => new(account.Id, account.Name, account.Type, account.IsArchived);
}
