using FolderCompare.Api.Models;
using FolderCompare.Api.Utilities;

namespace FolderCompare.Api.Services;

/// <summary>
/// Walks both scan trees in parallel by normalized name. Because folders, archives and archive
/// folders are all containers, the same recursion handles on-disk and in-archive comparison.
/// </summary>
public sealed class ComparisonTreeBuilder : IComparisonTreeBuilder
{
    private readonly IFileComparisonService _fileComparisonService;

    public ComparisonTreeBuilder(IFileComparisonService fileComparisonService)
    {
        _fileComparisonService = fileComparisonService;
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
        var root = await CompareAsync(left, right, "/", "/", comparer, settings, summary, onNodeCompared, isRoot: true, cancellationToken)
            .ConfigureAwait(false);

        return root;
    }

    private async Task<ComparisonNode> CompareAsync(
        ScanNode? left,
        ScanNode? right,
        string name,
        string relativePath,
        StringComparer comparer,
        CompareSettings settings,
        ComparisonSummary summary,
        Action<ComparisonNode>? onNodeCompared,
        bool isRoot,
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
                node.Children = BuildSingleSidedChildren(source, status, summary, onNodeCompared, cancellationToken);
            }

            Account(node, summary, isRoot);
            onNodeCompared?.Invoke(node);
            return node;
        }

        var leftIsContainer = left.IsContainer;
        var rightIsContainer = right.IsContainer;

        if (leftIsContainer != rightIsContainer)
        {
            // A file replaced by a folder (or the other way round) is reported as modified.
            node.Status = ComparisonStatus.Modified;
            node.CanCompareContent = false;
            node.ContentUnavailableReason = "The entry is a file on one side and a folder on the other.";
            Account(node, summary, isRoot);
            onNodeCompared?.Invoke(node);
            return node;
        }

        if (leftIsContainer)
        {
            // An archive we could not open has no children to compare, so fall back to
            // comparing the archive file itself instead of silently reporting it unchanged.
            if (source.Type == NodeType.Archive && (left.Error is not null || right.Error is not null))
            {
                var archivesEqual = await _fileComparisonService
                    .AreEqualAsync(left, right, settings.CalculateHashes, cancellationToken)
                    .ConfigureAwait(false);

                node.Status = archivesEqual ? ComparisonStatus.Unchanged : ComparisonStatus.Modified;
                node.CanCompareContent = false;
                node.ContentUnavailableReason = "The archive could not be inspected.";
                Account(node, summary, isRoot);
                onNodeCompared?.Invoke(node);
                return node;
            }

            var children = new List<ComparisonNode>();
            var keys = new HashSet<string>(comparer);

            if (left.Children is not null)
            {
                keys.UnionWith(left.Children.Keys);
            }

            if (right.Children is not null)
            {
                keys.UnionWith(right.Children.Keys);
            }

            foreach (var key in keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var leftChild = Lookup(left, key);
                var rightChild = Lookup(right, key);

                var childSource = rightChild ?? leftChild!;
                var childPath = childSource.RelativePath;

                var child = await CompareAsync(
                        leftChild,
                        rightChild,
                        childSource.Name,
                        childPath,
                        comparer,
                        settings,
                        summary,
                        onNodeCompared,
                        isRoot: false,
                        cancellationToken)
                    .ConfigureAwait(false);

                children.Add(child);
            }

            node.Children = Sort(children);
            node.Status = children.Any(child => child.Status != ComparisonStatus.Unchanged)
                ? ComparisonStatus.Modified
                : ComparisonStatus.Unchanged;
            node.CanCompareContent = false;
            node.ContentUnavailableReason = null;

            Account(node, summary, isRoot);
            onNodeCompared?.Invoke(node);
            return node;
        }

        var equal = await _fileComparisonService.AreEqualAsync(left, right, settings.CalculateHashes, cancellationToken).ConfigureAwait(false);
        node.Status = equal ? ComparisonStatus.Unchanged : ComparisonStatus.Modified;

        var eligibility = _fileComparisonService.EvaluateEligibility(left, right, node.Status);
        node.CanCompareContent = eligibility.CanCompare;
        node.ContentUnavailableReason = eligibility.Reason;

        Account(node, summary, isRoot);
        onNodeCompared?.Invoke(node);
        return node;
    }

    private List<ComparisonNode> BuildSingleSidedChildren(
        ScanNode source,
        ComparisonStatus status,
        ComparisonSummary summary,
        Action<ComparisonNode>? onNodeCompared,
        CancellationToken cancellationToken)
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
                node.Children = BuildSingleSidedChildren(child, status, summary, onNodeCompared, cancellationToken);
            }

            Account(node, summary, isRoot: false);
            onNodeCompared?.Invoke(node);
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
}
