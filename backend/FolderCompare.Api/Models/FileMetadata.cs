namespace FolderCompare.Api.Models;

/// <summary>
/// Normalized description of one entry produced by a scanner. The comparison engine works
/// exclusively with these nodes so that it never has to know about <see cref="FileInfo"/>,
/// <see cref="DirectoryInfo"/> or archive-specific types.
/// </summary>
public sealed class ScanNode
{
    public required string Name { get; init; }

    /// <summary>Root-relative normalized path, e.g. <c>/src/config.json</c> or <c>/pkg.zip!/a.txt</c>.</summary>
    public required string RelativePath { get; init; }

    public required NodeType Type { get; init; }

    public long? Size { get; init; }

    /// <summary>Absolute path on disk. Null for entries that live inside an archive.</summary>
    public string? AbsolutePath { get; init; }

    /// <summary>
    /// Path of the archive file on disk that ultimately contains this node, plus the entry
    /// segments needed to reach it. Null for on-disk nodes.
    /// </summary>
    public ArchiveEntryLocation? ArchiveLocation { get; init; }

    /// <summary>CRC32 taken from the archive directory; lets us detect changes without decompressing.</summary>
    public uint? Crc32 { get; init; }

    /// <summary>Non-fatal problem encountered while scanning this node.</summary>
    public string? Error { get; set; }

    /// <summary>Children keyed by name using the configured (case sensitive or insensitive) comparer.</summary>
    public Dictionary<string, ScanNode>? Children { get; set; }

    public bool IsContainer => Type is NodeType.Folder or NodeType.Archive or NodeType.ArchiveFolder;
}

/// <summary>Locates an entry inside a (possibly nested) archive.</summary>
/// <param name="ArchiveFilePath">Absolute path of the outermost archive on disk.</param>
/// <param name="EntrySegments">
/// One segment per archive level. The last segment addresses the entry itself, preceding
/// segments address nested archives, e.g. <c>["packages/inner.zip", "config.json"]</c>.
/// </param>
public sealed record ArchiveEntryLocation(string ArchiveFilePath, IReadOnlyList<string> EntrySegments);

/// <summary>Snapshot of a scanned root folder.</summary>
public sealed class FolderSnapshot
{
    public required string RootPath { get; init; }

    public required ScanNode Root { get; init; }

    public int FileCount { get; set; }

    public int FolderCount { get; set; }

    public int ArchiveCount { get; set; }

    public List<string> Warnings { get; } = new();
}
