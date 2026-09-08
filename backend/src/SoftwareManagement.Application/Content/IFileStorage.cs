namespace SoftwareManagement.Application.Content;

/// <summary>
/// Where uploaded bytes live. Local disk today (A-19); an object store later is one implementation,
/// not a migration, because nothing above this interface knows about paths.
/// </summary>
public interface IFileStorage
{
    Task<string> SaveAsync(Stream content, string extension, CancellationToken cancellationToken);

    Task<Stream?> OpenAsync(string storageKey, CancellationToken cancellationToken);

    Task DeleteAsync(string storageKey, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(string storageKey, CancellationToken cancellationToken);
}

/// <summary>
/// Decides what an uploaded file actually is by reading its first bytes.
///
/// The filename and the browser's content type are both attacker-controlled, so neither is
/// evidence. This is the check that stops an executable arriving as `logo.png` (NFR-SEC-06).
/// </summary>
public interface IFileTypeInspector
{
    /// <summary>Returns the detected type, or null when the bytes match nothing we accept.</summary>
    DetectedFileType? Detect(ReadOnlySpan<byte> header);
}

public sealed record DetectedFileType(string ContentType, string Extension, bool IsImage);

/// <summary>Produces the web-sized version and the thumbnail every uploaded image needs.</summary>
public interface IImageProcessor
{
    /// <summary>
    /// Resizes to fit within <paramref name="maxEdge"/> pixels, preserving aspect ratio, and never
    /// enlarges a smaller image. Returns null when the bytes are not a readable image.
    /// </summary>
    Task<ProcessedImage?> ResizeAsync(Stream source, int maxEdge, CancellationToken cancellationToken);
}

public sealed record ProcessedImage(Stream Content, int Width, int Height, string Extension);

/// <summary>Upload limits from NFR-DATA-03, in one place so the API and the tests agree.</summary>
public static class UploadLimits
{
    public const long MaxImageBytes = 10 * 1024 * 1024;
    public const long MaxDocumentBytes = 25 * 1024 * 1024;

    /// <summary>The longest edge of the stored web version. Larger originals are kept as uploaded.</summary>
    public const int WebImageMaxEdge = 1920;

    public const int ThumbnailMaxEdge = 400;
}
