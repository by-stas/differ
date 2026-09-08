namespace FolderCompare.Api.Models;

/// <summary>
/// A single node of the comparison tree. The same shape is used for folders, files,
/// archives and entries inside archives.
/// </summary>
public sealed class ComparisonNode
{
    public required string Name { get; init; }

    /// <summary>
    /// Normalized, root-relative path such as <c>/src/config.json</c>. Entries inside
    /// archives use the virtual form <c>/package.zip!/config.json</c>.
    /// </summary>
    public required string RelativePath { get; init; }

    public required NodeType Type { get; init; }

    public ComparisonStatus Status { get; set; }

    public long? LeftSize { get; init; }

    public long? RightSize { get; init; }

    /// <summary>True when the node can be opened in the diff viewer.</summary>
    public bool CanCompareContent { get; set; }

    /// <summary>Human readable explanation when <see cref="CanCompareContent"/> is false.</summary>
    public string? ContentUnavailableReason { get; set; }

    /// <summary>Non-fatal problem encountered while scanning this node (corrupt archive, access denied, ...).</summary>
    public string? Error { get; set; }

    public List<ComparisonNode>? Children { get; set; }
}
