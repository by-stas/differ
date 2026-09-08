using FolderCompare.Api.Configuration;
using FolderCompare.Api.Models;
using FolderCompare.Api.Utilities;
using Microsoft.Extensions.Options;

namespace FolderCompare.Api.Services;

/// <summary>
/// Walks both scan trees in parallel by normalized name. Because folders, archives and archive
/// folders are all containers, the same recursion handles on-disk and in-archive comparison.
/// </summary>
/// <remarks>
/// The tree is built in three passes: the structure is merged first, the expensive content
/// comparisons then run with bounded parallelism, and a final pass propagates container status
/// and fills in the summary. Splitting it this way keeps hashing off the critical path without
/// making the shared summary counters thread-affine.
/// </remarks>
public sealed class ComparisonTreeBuilder : IComparisonTreeBuilder
{
    private readonly IFileComparisonService _fileComparisonService;
    private readonly ComparisonOptions _options;

    public ComparisonTreeBuilder(IFileComparisonService fileComparisonService, IOptions<ComparisonOptions> options)
    {
        _fileComparisonService = fileComparisonService;
        _options = options.Value;
    }

    public async Task<ComparisonNode> BuildAsync(
        ScanNode left,
        ScanNode right,
        CompareSettings settings,
        ComparisonSummary summary,
        Action<ComparisonNode>? onNodeCompared = null,
        CancellationToken cancellationToken = default)
    {
        var comparer = PathHelper.GetComparer(settings.CaseSensitive);
        var pending = new List<LeafComparison>();

        var root = Merge(left, right, "/", "/", comparer, pending, cancellationToken);

        await ResolveLeavesAsync(pending, settings, cancellationToken).ConfigureAwait(false);

        Finalize(root, summary, onNodeCompared, isRoot: true, cancellationToken);

        return root;
    }

    /// <summary>Merges both trees into one, deferring the content comparison of matched files.</summary>
    private ComparisonNode Merge(
        ScanNode? left,
        ScanNode? right,
        string name,
        string relativePath,
        StringComparer comparer,
        List<LeafComparison> pending,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var source = right ?? left!;
        var node = new ComparisonNode
        {
            Name = name,
            RelativePath = relativePath,
            Type = source.Type,
            LeftSize = left?.Size,
            RightSize = right?.Size,
            Error = CombineErrors(left?.Error, right?.Error)
        };

        if (left is null || right is null)
        {
            var status = left is null ? ComparisonStatus.Added : ComparisonStatus.Removed;
            node.Status = status;
            node.CanCompareContent = false;
            node.ContentUnavailableReason = status == ComparisonStatus.Added
                ? "The file exists only in the right folder."
                : "The file exists only in the left folder.";

            if (source.IsContainer)
            {
                node.Children = BuildSingleSidedChildren(source, status, cancellationToken);
            }

            return node;
        }

        if (left.IsContainer != right.IsContainer)
        {
            // A file replaced by a folder (or the other way round) is reported as modified.
            node.Status = ComparisonStatus.Modified;
            node.CanCompareContent = false;
            node.ContentUnavailableReason = "The entry is a file on one side and a folder on the other.";
            return node;
        }

        if (!left.IsContainer)
        {
            pending.Add(new LeafComparison(node, left, right));
            return node;
        }

        // An archive we could not open has no children to compare, so its own bytes decide.
        if (source.Type == NodeType.Archive && (left.Error is not null || right.Error is not null))
        {
            pending.Add(new LeafComparison(node, left, right, "The archive could not be inspected."));
            return node;
        }

        var keys = new HashSet<string>(comparer);
        if (left.Children is not null)
        {
            keys.UnionWith(left.Children.Keys);
        }

        if (right.Children is not null)
        {
            keys.UnionWith(right.Children.Keys);
        }

        var children = new List<ComparisonNode>(keys.Count);
        foreach (var key in keys)
        {
            var leftChild = Lookup(left, key);
            var rightChild = Lookup(right, key);
            var childSource = rightChild ?? leftChild!;

            children.Add(Merge(
                leftChild,
                rightChild,
                childSource.Name,
                childSource.RelativePath,
                comparer,
                pending,
                cancellationToken));
        }

        node.Children = Sort(children);
        node.CanCompareContent = false;
        return node;
    }

    /// <summary>
    /// Runs the size/checksum/hash comparisons. Concurrency is capped so a large tree cannot
    /// saturate the disk with parallel reads.
    /// </summary>
    private async Task ResolveLeavesAsync(List<LeafComparison> pending, CompareSettings settings, CancellationToken cancellationToken)
    {
        if (pending.Count == 0)
        {
            return;
        }

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, _options.MaxDegreeOfParallelism),
            CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(pending, parallelOptions, async (leaf, token) =>
        {
            var equal = await _fileComparisonService
                .AreEqualAsync(leaf.Left, leaf.Right, settings.CalculateHashes, token)
                .ConfigureAwait(false);

            leaf.Node.Status = equal ? ComparisonStatus.Unchanged : ComparisonStatus.Modified;

            if (leaf.UnavailableReason is not null)
            {
                leaf.Node.CanCompareContent = false;
                leaf.Node.ContentUnavailableReason = leaf.UnavailableReason;
                return;
            }

            var eligibility = _fileComparisonService.EvaluateEligibility(leaf.Left, leaf.Right, leaf.Node.Status);
            leaf.Node.CanCompareContent = eligibility.CanCompare;
            leaf.Node.ContentUnavailableReason = eligibility.Reason;
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Propagates status up from the leaves and fills the summary. A container is modified as
    /// soon as one descendant differs.
    /// </summary>
    private static void Finalize(
        ComparisonNode node,
        ComparisonSummary summary,
        Action<ComparisonNode>? onNodeCompared,
        bool isRoot,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (node.Children is not null)
        {
            foreach (var child in node.Children)
            {
                Finalize(child, summary, onNodeCompared, isRoot: false, cancellationToken);
            }

            if (node.Status == ComparisonStatus.Unchanged && node.Children.Count > 0)
            {
                node.Status = node.Children.Any(child => child.Status != ComparisonStatus.Unchanged)
                    ? ComparisonStatus.Modified
                    : ComparisonStatus.Unchanged;
            }
        }

        Account(node, summary, isRoot);
        onNodeCompared?.Invoke(node);
    }

    private List<ComparisonNode> BuildSingleSidedChildren(ScanNode source, ComparisonStatus status, CancellationToken cancellationToken)
    {
        var children = new List<ComparisonNode>();
        if (source.Children is null)
        {
            return children;
        }

        foreach (var child in source.Children.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var node = new ComparisonNode
            {
                Name = child.Name,
                RelativePath = child.RelativePath,
                Type = child.Type,
                Status = status,
                LeftSize = status == ComparisonStatus.Removed ? child.Size : null,
                RightSize = status == ComparisonStatus.Added ? child.Size : null,
                CanCompareContent = false,
                ContentUnavailableReason = status == ComparisonStatus.Added
                    ? "The file exists only in the right folder."
                    : "The file exists only in the left folder.",
                Error = child.Error
            };

            if (child.IsContainer)
            {
                node.Children = BuildSingleSidedChildren(child, status, cancellationToken);
            }

            children.Add(node);
        }

        return Sort(children);
    }

    private static ScanNode? Lookup(ScanNode parent, string key)
        => parent.Children is not null && parent.Children.TryGetValue(key, out var child) ? child : null;

    /// <summary>Containers first, then files, each group sorted by name.</summary>
    private static List<ComparisonNode> Sort(List<ComparisonNode> nodes)
        => nodes
            .OrderByDescending(node => node.Type is NodeType.Folder or NodeType.Archive or NodeType.ArchiveFolder)
            .ThenBy(node => node.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static void Account(ComparisonNode node, ComparisonSummary summary, bool isRoot)
    {
        if (isRoot)
        {
            return;
        }

        summary.Total++;

        switch (node.Status)
        {
            case ComparisonStatus.Added:
                summary.Added++;
                break;
            case ComparisonStatus.Removed:
                summary.Removed++;
                break;
            case ComparisonStatus.Modified:
                summary.Modified++;
                break;
            default:
                summary.Unchanged++;
                break;
        }

        switch (node.Type)
        {
            case NodeType.Folder:
                summary.Folders++;
                break;
            case NodeType.File:
                summary.Files++;
                break;
            case NodeType.Archive:
                summary.Archives++;
                break;
            default:
                summary.ArchiveEntries++;
                break;
        }
    }

    private static string? CombineErrors(string? left, string? right)
    {
        if (left is null && right is null)
        {
            return null;
        }

        if (left is null)
        {
            return right;
        }

        if (right is null)
        {
            return left;
        }

        return string.Equals(left, right, StringComparison.Ordinal) ? left : $"{left} / {right}";
    }

    /// <param name="UnavailableReason">Set for archives that could not be opened and are therefore compared as plain files.</param>
    private sealed record LeafComparison(ComparisonNode Node, ScanNode Left, ScanNode Right, string? UnavailableReason = null);
}
