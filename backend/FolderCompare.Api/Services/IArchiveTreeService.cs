using FolderCompare.Api.Models;

namespace FolderCompare.Api.Services;

/// <summary>
/// Turns archives into virtual folder trees of <see cref="ScanNode"/> and resolves the content
/// of entries inside (possibly nested) archives.
/// </summary>
public interface IArchiveTreeService
{
    /// <summary>True when any registered scanner recognises the file.</summary>
    bool IsArchive(string fileName);

    /// <summary>
    /// Builds the node for an archive found on disk, including its children when
    /// <paramref name="maxDepth"/> allows it. Failures are reported through
    /// <see cref="ScanNode.Error"/> rather than thrown, so a single bad archive cannot fail
    /// the whole comparison.
    /// </summary>
    Task<ScanNode> BuildArchiveNodeAsync(
        string absolutePath,
        string relativePath,
        string name,
        long size,
        int maxDepth,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the content of an archive entry into a seekable stream.</summary>
    Task<Stream> ReadEntryContentAsync(ArchiveEntryLocation location, long maxBytes, CancellationToken cancellationToken = default);
}
