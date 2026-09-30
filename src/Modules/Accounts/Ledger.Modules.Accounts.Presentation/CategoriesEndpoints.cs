using Ledger.BuildingBlocks.Application;
using Ledger.Modules.Accounts.Application.Categories;
using Ledger.Modules.Accounts.Domain;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Ledger.Modules.Accounts.Presentation;

public sealed record CreateCategoryRequest(string Name, AccountType Type);

public sealed record RenameCategoryRequest(string Name);

public static class CategoriesEndpoints
{
    public static IEndpointRouteBuilder MapAccountsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/api/categories")
            .WithTags("Categories")
            .RequireAuthorization();

        group.MapGet("/", async (bool? includeArchived, ICurrentUser user, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new GetCategoriesQuery(user.UserId, includeArchived ?? false), ct)))
            .WithSummary("List my income and expense categories");

        group.MapPost("/", async (CreateCategoryRequest request, ICurrentUser user, ISender sender, CancellationToken ct) =>
            {
                var category = await sender.Send(
                    new CreateCategoryCommand(user.UserId, request.Name, request.Type), ct);

                return Results.Created($"/api/categories/{category.Id}", category);
            })
            .WithSummary("Create a category (Income or Expense)");

        group.MapPatch("/{id:guid}", async (Guid id, RenameCategoryRequest request, ICurrentUser user, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new RenameCategoryCommand(user.UserId, id, request.Name), ct);
                return Results.NoContent();
            })
            .WithSummary("Rename a category");

        group.MapPost("/{id:guid}/archive", async (Guid id, ICurrentUser user, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new ArchiveCategoryCommand(user.UserId, id), ct);
                return Results.NoContent();
            })
            .WithSummary("Archive a category");

        return endpoints;
    }
}
