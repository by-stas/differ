using FolderCompare.Api.Models;

namespace FolderCompare.Api.Services;

/// <param name="CaseSensitive">Whether entry names are matched case-sensitively.</param>
/// <param name="CalculateHashes">Whether same-size files are additionally compared by content.</param>
public sealed record CompareSettings(bool CaseSensitive, bool CalculateHashes);

/// <summary>Merges two scan trees into a single annotated comparison tree.</summary>
public interface IComparisonTreeBuilder
{
    Task<ComparisonNode> BuildAsync(
        ScanNode left,
        ScanNode right,
        CompareSettings settings,
        ComparisonSummary summary,
        Action<ComparisonNode>? onNodeCompared = null,
        CancellationToken cancellationToken = default);
}
