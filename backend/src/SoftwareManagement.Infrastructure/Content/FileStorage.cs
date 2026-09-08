using Microsoft.Extensions.Configuration;
using SkiaSharp;
using SoftwareManagement.Application.Content;

namespace SoftwareManagement.Infrastructure.Content;

/// <summary>
/// Stores uploads on local disk under a configured root (A-19).
///
/// The storage key is generated, never derived from the uploaded filename, so a name like
/// `..\..\appsettings.json` cannot escape the folder. Files are spread across two levels of
/// directories so no single folder ends up with ten thousand entries.
/// </summary>
public sealed class LocalFileStorage : IFileStorage
{
    private readonly string _root;

    public LocalFileStorage(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        _root = configuration["Media:RootPath"]
            ?? Path.Combine(AppContext.BaseDirectory, "media");

        Directory.CreateDirectory(_root);
    }

    public async Task<string> SaveAsync(Stream content, string extension, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        var id = Guid.NewGuid().ToString("N");
        var key = $"{id[..2]}/{id[2..4]}/{id}{extension}";
        var path = ResolvePath(key);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        await using var file = File.Create(path);
        await content.CopyToAsync(file, cancellationToken).ConfigureAwait(false);

        return key;
    }

    public Task<Stream?> OpenAsync(string storageKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(storageKey);

        var path = ResolvePath(storageKey);
        Stream? stream = File.Exists(path) ? File.OpenRead(path) : null;
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(storageKey);

        var path = ResolvePath(storageKey);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string storageKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(storageKey);

        return Task.FromResult(File.Exists(ResolvePath(storageKey)));
    }

    private string ResolvePath(string storageKey)
    {
        var full = Path.GetFullPath(Path.Combine(_root, storageKey.Replace('/', Path.DirectorySeparatorChar)));

        if (!full.StartsWith(Path.GetFullPath(_root), StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Storage key resolves outside the media root.");
        }

        return full;
    }
}

/// <summary>
/// Identifies an upload by its magic number (NFR-SEC-06).
///
/// Only these types are accepted. Anything else, including an executable renamed to .png, is
/// rejected before a single byte is stored.
/// </summary>
public sealed class MagicNumberFileTypeInspector : IFileTypeInspector
{
    private static readonly (byte[] Signature, int Offset, DetectedFileType Type)[] Signatures =
    [
        ([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], 0, new DetectedFileType("image/png", ".png", true)),
        ([0xFF, 0xD8, 0xFF], 0, new DetectedFileType("image/jpeg", ".jpg", true)),
        ([0x47, 0x49, 0x46, 0x38], 0, new DetectedFileType("image/gif", ".gif", true)),
        ([0x52, 0x49, 0x46, 0x46], 0, new DetectedFileType("image/webp", ".webp", true)),
        ([0x25, 0x50, 0x44, 0x46, 0x2D], 0, new DetectedFileType("application/pdf", ".pdf", false)),
    ];

    public DetectedFileType? Detect(ReadOnlySpan<byte> header)
    {
        foreach (var (signature, offset, type) in Signatures)
        {
            if (header.Length < offset + signature.Length)
            {
                continue;
            }

            if (header.Slice(offset, signature.Length).SequenceEqual(signature))
            {
                // WEBP shares the RIFF header with other formats; the format tag is at byte 8.
                if (type.Extension == ".webp"
                    && (header.Length < 12 || !header.Slice(8, 4).SequenceEqual("WEBP"u8)))
                {
                    continue;
                }

                return type;
            }
        }

        // SVG is text, so it has no magic number, and it can carry script. It is deliberately not
        // accepted: an uploaded SVG is an XSS vector on the page that displays it.
        return null;
    }
}

/// <summary>
/// Resizes uploaded images with SkiaSharp (ADR-21).
///
/// A 6000-pixel camera photograph on a product page costs every visitor several megabytes, so a
/// web-sized version and a thumbnail are produced at upload time and the original is kept
/// untouched (EX-110).
/// </summary>
public sealed class SkiaImageProcessor : IImageProcessor
{
    public async Task<ProcessedImage?> ResizeAsync(Stream source, int maxEdge, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        using var buffer = new MemoryStream();
        await source.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        buffer.Position = 0;

        using var original = SKBitmap.Decode(buffer);
        if (original is null)
        {
            return null;
        }

        var longestEdge = Math.Max(original.Width, original.Height);

        // Never enlarge: upscaling a small logo produces a blurry logo, not a better one.
        var scale = longestEdge <= maxEdge ? 1.0 : (double)maxEdge / longestEdge;
        var width = Math.Max(1, (int)Math.Round(original.Width * scale));
        var height = Math.Max(1, (int)Math.Round(original.Height * scale));

        using var resized = original.Resize(new SKImageInfo(width, height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        if (resized is null)
        {
            return null;
        }

        using var image = SKImage.FromBitmap(resized);
        var encoded = image.Encode(SKEncodedImageFormat.Webp, 82);

        var output = new MemoryStream();
        encoded.SaveTo(output);
        output.Position = 0;

        return new ProcessedImage(output, width, height, ".webp");
    }
}
