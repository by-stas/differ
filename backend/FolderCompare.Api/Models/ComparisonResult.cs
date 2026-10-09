namespace FolderCompare.Api.Models;

public sealed class ComparisonSummary
{
    public int Total { get; set; }
    public int Added { get; set; }
    public int Removed { get; set; }
    public int Modified { get; set; }
    public int Unchanged { get; set; }

    public int Folders { get; set; }
    public int Files { get; set; }
    public int Archives { get; set; }
    public int ArchiveEntries { get; set; }
}

/// <summary>
/// Coarse progress information so the UI can show something meaningful while a large
/// comparison is still running.
/// </summary>
public sealed class ComparisonProgress
{
    public string Phase { get; set; } = "Pending";
    public int ScannedFiles { get; set; }
    public int ScannedFolders { get; set; }
    public int ScannedArchives { get; set; }
    public int ComparedNodes { get; set; }
    public string? CurrentPath { get; set; }
}

public sealed class ComparisonResult
{
    public required string Id { get; init; }

    public ComparisonState Status { get; set; } = ComparisonState.Pending;

    public required string LeftPath { get; init; }

    public required string RightPath { get; init; }

    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? CompletedAt { get; set; }

    public long? DurationMs { get; set; }

    public ComparisonSummary? Summary { get; set; }

    public ComparisonProgress Progress { get; set; } = new();

    public ComparisonNode? Root { get; set; }

    /// <summary>Non-fatal problems (unreadable folders, corrupt archives, ...).</summary>
    public List<string> Warnings { get; set; } = new();

    public ApiError? Error { get; set; }
}
