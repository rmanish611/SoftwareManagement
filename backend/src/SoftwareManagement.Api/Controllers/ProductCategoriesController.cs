using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SoftwareManagement.Application.Common;
using SoftwareManagement.Application.Security;
using SoftwareManagement.Domain.Catalog;
using SoftwareManagement.Domain.Content;
using SoftwareManagement.Infrastructure.Persistence;

namespace SoftwareManagement.Api.Controllers;

/// <summary>
/// The industries the catalogue is grouped by. A buyer arrives with a job rather than a product
/// name, so the category is how they find anything (REQ-CAT-002).
/// </summary>
[ApiController]
[Route("api/v1/admin/product-categories")]
public sealed class ProductCategoriesController(AppDbContext dbContext) : ControllerBase
{
    private readonly AppDbContext _dbContext = dbContext;

    [HttpGet]
    [Authorize(Policy = Permissions.Catalog.ProductRead)]
    public async Task<ActionResult<IReadOnlyList<CategorySummary>>> List(CancellationToken cancellationToken)
    {
        var categories = await _dbContext.ProductCategories
            .AsNoTracking()
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new CategorySummary(
                c.Id,
                c.Name,
                c.Slug,
                c.Description,
                c.IconKey,
                c.SortOrder,
                c.IsPublished,
                c.Products.Count))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return Ok(categories);
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Catalog.ProductWrite)]
    public async Task<ActionResult<CategorySummary>> Create(CategoryBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        var slug = Slug.From(string.IsNullOrWhiteSpace(body.Slug) ? body.Name : body.Slug);

        if (!Slug.IsValid(slug))
        {
            return Problem(
                title: "Slug is not valid",
                detail: $"A slug is {Slug.MinLength} to {Slug.MaxLength} characters, lowercase letters, digits and single hyphens.",
                statusCode: StatusCodes.Status422UnprocessableEntity,
                type: "https://softwaremanagement.example/errors/invalid-slug",
                extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = "INVALID_SLUG" });
        }

        if (await _dbContext.ProductCategories.AnyAsync(c => c.Slug == slug, cancellationToken).ConfigureAwait(false))
        {
            return Problem(
                title: "Slug is taken",
                detail: $"A category already uses the address \"{slug}\".",
                statusCode: StatusCodes.Status409Conflict,
                type: "https://softwaremanagement.example/errors/slug-taken",
                extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = "SLUG_TAKEN" });
        }

        var used = await _dbContext.ProductCategories.CountAsync(cancellationToken).ConfigureAwait(false);

        var category = new ProductCategory
        {
            Id = Guid.NewGuid(),
            Name = body.Name.Trim(),
            Slug = slug,
            Description = body.Description,
            IconKey = body.IconKey,
            SortOrder = used + 1,
            IsPublished = body.IsPublished,
            CreatedBy = ActorEmail(),
        };

        _dbContext.ProductCategories.Add(category);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Ok(new CategorySummary(
            category.Id,
            category.Name,
            category.Slug,
            category.Description,
            category.IconKey,
            category.SortOrder,
            category.IsPublished,
            0));
    }

    /// <summary>
    /// Deleting a category that still holds published products would take those products off the
    /// public site as a side effect of tidying up a list. It is refused; move the products first
    /// (REQ-CAT-002).
    /// </summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Permissions.Catalog.ProductArchive)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var category = await _dbContext.ProductCategories
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken).ConfigureAwait(false);

        if (category is null)
        {
            return NotFound();
        }

        var published = await _dbContext.Products
            .CountAsync(
                p => p.CategoryId == id && (p.Status == ContentStatus.Published || p.Status == ContentStatus.Modified),
                cancellationToken)
            .ConfigureAwait(false);

        if (published > 0)
        {
            return Problem(
                title: "The category is still in use",
                detail: $"{published} published product(s) are in this category. Move them to another category first.",
                statusCode: StatusCodes.Status409Conflict,
                type: "https://softwaremanagement.example/errors/category-in-use",
                extensions: new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["code"] = "CATEGORY_IN_USE",
                    ["publishedProductCount"] = published,
                });
        }

        var anyProducts = await _dbContext.Products
            .AnyAsync(p => p.CategoryId == id, cancellationToken).ConfigureAwait(false);

        if (anyProducts)
        {
            return Problem(
                title: "The category is still in use",
                detail: "Draft products are still in this category. Move them to another category first.",
                statusCode: StatusCodes.Status409Conflict,
                type: "https://softwaremanagement.example/errors/category-in-use",
                extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = "CATEGORY_IN_USE" });
        }

        _dbContext.ProductCategories.Remove(category);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return NoContent();
    }

    private string ActorEmail() => User.FindFirstValue(ClaimTypes.Email) ?? "unknown";
}

public sealed record CategoryBody(string Name, string? Slug, string? Description, string? IconKey, bool IsPublished);

public sealed record CategorySummary(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    string? IconKey,
    int SortOrder,
    bool IsPublished,
    int ProductCount);
