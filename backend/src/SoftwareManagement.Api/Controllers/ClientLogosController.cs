using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SoftwareManagement.Application.Security;
using SoftwareManagement.Domain.Portfolio;
using SoftwareManagement.Infrastructure.Persistence;

namespace SoftwareManagement.Api.Controllers;

/// <summary>
/// Client logos, and the permission that decides whether they may be shown.
///
/// Recording the permission is a separate act from recording the logo, and it has its own
/// permission to perform, because it is the point at which a promise to a client is made or
/// withdrawn (BR-PRJ-01).
/// </summary>
[ApiController]
[Route("api/v1/admin/client-logos")]
public sealed class ClientLogosController(AppDbContext dbContext) : ControllerBase
{
    private readonly AppDbContext _dbContext = dbContext;

    [HttpGet]
    [Authorize(Policy = Permissions.Portfolio.ProjectRead)]
    public async Task<ActionResult<IReadOnlyList<ClientLogoRow>>> List(CancellationToken cancellationToken)
    {
        var logos = await _dbContext.ClientLogos
            .AsNoTracking()
            .OrderBy(l => l.SortOrder).ThenBy(l => l.DisplayName)
            .Select(l => new ClientLogoRow(l.Id, l.DisplayName, l.MediaAssetId, l.HasPermission, l.SortOrder))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return Ok(logos);
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Portfolio.ClientPublish)]
    public async Task<ActionResult<ClientLogoRow>> Create(ClientLogoBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        if (string.IsNullOrWhiteSpace(body.DisplayName))
        {
            return Refuse("NAME_REQUIRED", "displayName", "Record who this logo belongs to.");
        }

        if (!await _dbContext.MediaAssets.AnyAsync(a => a.Id == body.MediaAssetId, cancellationToken).ConfigureAwait(false))
        {
            return Refuse("UNKNOWN_ASSET", "mediaAssetId", "Upload the image first, then record the logo.");
        }

        var logo = new ClientLogo
        {
            Id = Guid.NewGuid(),
            OrganisationId = body.OrganisationId,
            DisplayName = body.DisplayName.Trim(),
            MediaAssetId = body.MediaAssetId,

            // Permission defaults to absent. A logo recorded in a hurry shows the anonymised label
            // until somebody says otherwise, which is the only safe way round.
            HasPermission = body.HasPermission,
            SortOrder = body.SortOrder,
            CreatedBy = Actor(),
        };

        _dbContext.ClientLogos.Add(logo);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Created(
            $"/api/v1/admin/client-logos/{logo.Id}",
            new ClientLogoRow(logo.Id, logo.DisplayName, logo.MediaAssetId, logo.HasPermission, logo.SortOrder));
    }

    /// <summary>Granting or withdrawing the client's agreement to be named.</summary>
    [HttpPut("{id:guid}/permission")]
    [Authorize(Policy = Permissions.Portfolio.ClientPublish)]
    public async Task<IActionResult> SetPermission(Guid id, PermissionBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        var logo = await _dbContext.ClientLogos.FirstOrDefaultAsync(l => l.Id == id, cancellationToken).ConfigureAwait(false);

        if (logo is null)
        {
            return NotFound();
        }

        logo.HasPermission = body.HasPermission;
        logo.ModifiedBy = Actor();

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return NoContent();
    }

    private ObjectResult Refuse(string code, string field, string message) => Problem(
        title: "That will not do",
        detail: message,
        statusCode: StatusCodes.Status422UnprocessableEntity,
        type: PortfolioErrors.Type(code),
        extensions: PortfolioErrors.Extensions(code, field, null));

    private string Actor() => User.FindFirstValue(ClaimTypes.Email) ?? "unknown";
}

public sealed record ClientLogoBody(
    string DisplayName,
    Guid MediaAssetId,
    bool HasPermission,
    int SortOrder,
    Guid? OrganisationId);

public sealed record PermissionBody(bool HasPermission);

public sealed record ClientLogoRow(Guid Id, string DisplayName, Guid MediaAssetId, bool HasPermission, int SortOrder);
