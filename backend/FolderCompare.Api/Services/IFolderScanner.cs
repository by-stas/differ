using FolderCompare.Api.Models;

namespace FolderCompare.Api.Services;

/// <param name="ArchiveMaxDepth">0 disables archive inspection; 1 scans archives on disk; 2 also scans archives nested inside them.</param>
/// <param name="CaseSensitive">Whether names are compared case-sensitively.</param>
/// <param name="FollowSymlinks">Whether reparse points are traversed.</param>
public sealed record ScanSettings(int ArchiveMaxDepth, bool CaseSensitive, bool FollowSymlinks);

public readonly record struct ScanProgressUpdate(NodeType Kind, string RelativePath);

/// <summary>
/// Walks a folder recursively and produces normalized <see cref="ScanNode"/> metadata.
/// File contents are never read during a scan.
/// </summary>
public interface IFolderScanner
{
    Task<FolderSnapshot> ScanAsync(
        string rootPath,
        ScanSettings settings,
        IProgress<ScanProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default);
}
