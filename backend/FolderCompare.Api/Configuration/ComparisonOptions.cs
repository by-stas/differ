namespace FolderCompare.Api.Configuration;

public sealed class ComparisonOptions
{
    public const string SectionName = "Comparison";

    /// <summary>Hard limit enforced by the backend before any file is opened for diffing.</summary>
    public long MaxDiffFileSizeBytes { get; set; } = 5 * 1024 * 1024;

    /// <summary>Compare SHA-256 hashes for same-size files instead of assuming they are identical.</summary>
    public bool CalculateHashes { get; set; } = true;

    /// <summary>
    /// 0 disables archive inspection, 1 scans archives found on disk, 2 additionally scans
    /// archives nested one level inside them, and so on.
    /// </summary>
    public int ArchiveMaxDepth { get; set; } = 1;

    /// <summary>Maximum number of entries read from a single archive.</summary>
    public int MaxArchiveEntries { get; set; } = 10_000;

    /// <summary>Archives larger than this are reported but not opened.</summary>
    public long MaxArchiveSizeBytes { get; set; } = 1L * 1024 * 1024 * 1024;

    /// <summary>Declared uncompressed size above which a single entry is skipped.</summary>
    public long MaxArchiveEntrySizeBytes { get; set; } = 256L * 1024 * 1024;

    /// <summary>Total declared uncompressed size above which an archive is rejected as a zip bomb.</summary>
    public long MaxTotalUncompressedBytes { get; set; } = 4L * 1024 * 1024 * 1024;

    /// <summary>Declared uncompressed/compressed ratio above which an archive is rejected.</summary>
    public double MaxCompressionRatio { get; set; } = 500;

    /// <summary>Nested archives are buffered in memory; larger ones are not expanded.</summary>
    public long MaxNestedArchiveBufferBytes { get; set; } = 64L * 1024 * 1024;

    /// <summary>
    /// null selects the platform default: case-insensitive on Windows/macOS, case-sensitive elsewhere.
    /// </summary>
    public bool? CaseSensitive { get; set; }

    /// <summary>Reparse points are skipped by default to avoid cycles and escapes from the root.</summary>
    public bool FollowSymlinks { get; set; }

    public int MaxDegreeOfParallelism { get; set; } = 4;

    /// <summary>
    /// When non-empty, both comparison roots must live inside one of these folders.
    /// Strongly recommended whenever the API is reachable by more than one user.
    /// </summary>
    public List<string> AllowedRoots { get; set; } = new();

    /// <summary>
    /// How long POST /api/comparisons waits for a result before returning a running job the
    /// client can poll.
    /// </summary>
    public int SynchronousWaitMs { get; set; } = 3_000;

    /// <summary>Number of comparison results kept in memory.</summary>
    public int MaxStoredComparisons { get; set; } = 20;

    /// <summary>Results older than this are evicted.</summary>
    public TimeSpan ResultRetention { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Comparisons are abandoned after this long.</summary>
    public TimeSpan ComparisonTimeout { get; set; } = TimeSpan.FromMinutes(30);

    public List<string> CorsOrigins { get; set; } = new() { "http://localhost:4200" };
}
