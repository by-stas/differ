using FolderCompare.Api.Models;

namespace FolderCompare.Api.Services;

/// <summary>
/// Format-specific archive reader. Implementations must open archives read-only, must never
/// extract to the filesystem and must enforce the configured safety limits.
/// Additional formats (7z, RAR, TAR, ...) are added by registering another implementation.
/// </summary>
public interface IArchiveScanner
{
    /// <summary>Display name of the format, used in log messages and errors.</summary>
    string FormatName { get; }

    /// <summary>True when this scanner recognises the given file name or path.</summary>
    bool CanHandle(string filePath);

    /// <summary>Lists the entries of an archive stored on disk.</summary>
    Task<IReadOnlyCollection<ArchiveEntryInfo>> ScanAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the entries of an archive available as a seekable stream. This overload is what
    /// makes nested archives possible: the caller owns and disposes the stream.
    /// </summary>
    Task<IReadOnlyCollection<ArchiveEntryInfo>> ScanAsync(Stream archiveStream, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a single entry into a seekable in-memory stream, refusing to read more than
    /// <paramref name="maxBytes"/> so that a lying archive directory cannot exhaust memory.
    /// </summary>
    Task<Stream> ReadEntryAsync(Stream archiveStream, string entryPath, long maxBytes, CancellationToken cancellationToken = default);
}
