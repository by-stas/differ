namespace FolderCompare.Api.Models;

/// <summary>
/// Kind of entry in a comparison tree. Archive entries mirror the on-disk kinds so that
/// the comparison engine can treat archives as virtual folders.
/// </summary>
public enum NodeType
{
    Folder,
    File,
    Archive,
    ArchiveFolder,
    ArchiveFile
}

public enum ComparisonStatus
{
    Unchanged,
    Added,
    Removed,
    Modified
}

/// <summary>
/// Lifecycle of a comparison job.
/// </summary>
public enum ComparisonState
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled
}
