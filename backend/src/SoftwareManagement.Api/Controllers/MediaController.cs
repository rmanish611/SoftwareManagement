using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SoftwareManagement.Application.Content;
using SoftwareManagement.Application.Security;
using SoftwareManagement.Domain.Content;
using SoftwareManagement.Infrastructure.Persistence;

namespace SoftwareManagement.Api.Controllers;

/// <summary>
/// The media library.
///
/// An upload is accepted only after its bytes have been inspected: the filename and the browser's
/// content type are both attacker-controlled (NFR-SEC-06). Images get a web-sized version and a
/// thumbnail at upload time so a page never serves a six-megapixel photograph (EX-110).
/// </summary>
[ApiController]
[Route("api/v1/admin/media")]
public sealed class MediaController(
    AppDbContext dbContext,
    IFileStorage storage,
    IFileTypeInspector inspector,
    IImageProcessor images) : ControllerBase
{
    private const int HeaderBytes = 16;

    private readonly AppDbContext _dbContext = dbContext;
    private readonly IFileStorage _storage = storage;
    private readonly IFileTypeInspector _inspector = inspector;
    private readonly IImageProcessor _images = images;

    [HttpGet]
    [Authorize(Policy = Permissions.Content.MediaRead)]
    public async Task<ActionResult<IReadOnlyList<MediaSummary>>> List(
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var take = Math.Clamp(pageSize, 1, 100);

        var assets = await _dbContext.MediaAssets
            .AsNoTracking()
            .OrderByDescending(m => m.CreatedAtUtc)
            .Take(take)
            .Select(m => new MediaSummary(m.Id, m.FileName, m.ContentType, m.SizeBytes, m.Width, m.Height, m.AltText, m.Kind.ToString()))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return Ok(assets);
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Content.MediaWrite)]
    [RequestSizeLimit(UploadLimits.MaxDocumentBytes)]
    public async Task<ActionResult<MediaSummary>> Upload(IFormFile file, [FromForm] string? altText, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return Problem(
                title: "No file",
                detail: "Choose a file to upload.",
                statusCode: StatusCodes.Status400BadRequest,
                type: "https://softwaremanagement.example/errors/no-file");
        }

        // The size check comes first, so an oversized file is refused before a byte is written
        // (EX-108). RequestSizeLimit stops the truly enormous ones even earlier.
        if (file.Length > UploadLimits.MaxDocumentBytes)
        {
            return Problem(
                title: "File is too large",
                detail: $"The maximum is {UploadLimits.MaxDocumentBytes / (1024 * 1024)} MB for a document and {UploadLimits.MaxImageBytes / (1024 * 1024)} MB for an image.",
                statusCode: StatusCodes.Status413PayloadTooLarge,
                type: "https://softwaremanagement.example/errors/file-too-large");
        }

        await using var upload = file.OpenReadStream();
        var header = new byte[HeaderBytes];
        var read = await upload.ReadAtLeastAsync(header, HeaderBytes, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);

        var detected = _inspector.Detect(header.AsSpan(0, read));
        if (detected is null)
        {
            return Problem(
                title: "File type not accepted",
                detail: "Uploads must be a PNG, JPEG, GIF, WEBP or PDF. The file's contents decide, not its name.",
                statusCode: StatusCodes.Status415UnsupportedMediaType,
                type: "https://softwaremanagement.example/errors/unsupported-media-type");
        }

        if (detected.IsImage && file.Length > UploadLimits.MaxImageBytes)
        {
            return Problem(
                title: "Image is too large",
                detail: $"The maximum for an image is {UploadLimits.MaxImageBytes / (1024 * 1024)} MB.",
                statusCode: StatusCodes.Status413PayloadTooLarge,
                type: "https://softwaremanagement.example/errors/file-too-large");
        }

        upload.Position = 0;
        using var hashing = new MemoryStream();
        await upload.CopyToAsync(hashing, cancellationToken).ConfigureAwait(false);
        hashing.Position = 0;
        var sha256 = Convert.ToHexString(await SHA256.HashDataAsync(hashing, cancellationToken).ConfigureAwait(false));

        hashing.Position = 0;
        var storageKey = await _storage.SaveAsync(hashing, detected.Extension, cancellationToken).ConfigureAwait(false);

        string? webKey = null;
        string? thumbKey = null;
        int? width = null;
        int? height = null;

        if (detected.IsImage)
        {
            hashing.Position = 0;
            var web = await _images.ResizeAsync(hashing, UploadLimits.WebImageMaxEdge, cancellationToken).ConfigureAwait(false);
            if (web is not null)
            {
                webKey = await _storage.SaveAsync(web.Content, web.Extension, cancellationToken).ConfigureAwait(false);
                width = web.Width;
                height = web.Height;
                await web.Content.DisposeAsync().ConfigureAwait(false);
            }

            hashing.Position = 0;
            var thumb = await _images.ResizeAsync(hashing, UploadLimits.ThumbnailMaxEdge, cancellationToken).ConfigureAwait(false);
            if (thumb is not null)
            {
                thumbKey = await _storage.SaveAsync(thumb.Content, thumb.Extension, cancellationToken).ConfigureAwait(false);
                await thumb.Content.DisposeAsync().ConfigureAwait(false);
            }
        }

        var asset = new MediaAsset
        {
            Id = Guid.NewGuid(),
            FileName = Path.GetFileName(file.FileName),
            StorageKey = storageKey,
            WebStorageKey = webKey,
            ThumbnailStorageKey = thumbKey,
            ContentType = detected.ContentType,
            SizeBytes = file.Length,
            Width = width,
            Height = height,
            AltText = altText,
            Sha256 = sha256,
            Kind = detected.IsImage ? MediaKind.Image : MediaKind.Document,
            CreatedBy = ActorEmail(),
        };

        _dbContext.MediaAssets.Add(asset);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return CreatedAtAction(nameof(List), new { id = asset.Id },
            new MediaSummary(asset.Id, asset.FileName, asset.ContentType, asset.SizeBytes, asset.Width, asset.Height, asset.AltText, asset.Kind.ToString()));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Permissions.Content.MediaDelete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var asset = await _dbContext.MediaAssets.FirstOrDefaultAsync(m => m.Id == id, cancellationToken).ConfigureAwait(false);
        if (asset is null)
        {
            return NotFound();
        }

        // Deleting an image a live page shows would break that page, so it is refused with the
        // pages named (EX-112).
        var usedBy = await _dbContext.PageSections
            .Where(s => s.MediaAssetId == id)
            .Select(s => s.Page!.Title)
            .Distinct()
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        if (usedBy.Count > 0)
        {
            return Problem(
                title: "Media is in use",
                detail: "Remove it from these pages first: " + string.Join(", ", usedBy),
                statusCode: StatusCodes.Status409Conflict,
                type: "https://softwaremanagement.example/errors/media-in-use");
        }

        await _storage.DeleteAsync(asset.StorageKey, cancellationToken).ConfigureAwait(false);
        if (asset.WebStorageKey is not null) { await _storage.DeleteAsync(asset.WebStorageKey, cancellationToken).ConfigureAwait(false); }
        if (asset.ThumbnailStorageKey is not null) { await _storage.DeleteAsync(asset.ThumbnailStorageKey, cancellationToken).ConfigureAwait(false); }

        _dbContext.MediaAssets.Remove(asset);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return NoContent();
    }

    private string ActorEmail() => User.FindFirstValue(ClaimTypes.Email) ?? "unknown";
}

public sealed record MediaSummary(
    Guid Id,
    string FileName,
    string ContentType,
    long SizeBytes,
    int? Width,
    int? Height,
    string? AltText,
    string Kind);
