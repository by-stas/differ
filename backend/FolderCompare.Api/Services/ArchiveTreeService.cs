using FolderCompare.Api.Configuration;
using FolderCompare.Api.Infrastructure;
using FolderCompare.Api.Models;
using FolderCompare.Api.Utilities;
using Microsoft.Extensions.Options;

namespace FolderCompare.Api.Services;

/// <inheritdoc />
public sealed class ArchiveTreeService : IArchiveTreeService
{
    private readonly IReadOnlyList<IArchiveScanner> _scanners;
    private readonly ComparisonOptions _options;
    private readonly ILogger<ArchiveTreeService> _logger;
    private readonly StringComparer _childComparer;

    public ArchiveTreeService(
        IEnumerable<IArchiveScanner> scanners,
        IOptions<ComparisonOptions> options,
        ILogger<ArchiveTreeService> logger)
    {
        _scanners = scanners.ToList();
        _options = options.Value;
        _logger = logger;
        _childComparer = PathHelper.GetComparer(PathHelper.ResolveCaseSensitivity(_options));
    }

    public bool IsArchive(string fileName) => _scanners.Any(scanner => scanner.CanHandle(fileName));

    public async Task<ScanNode> BuildArchiveNodeAsync(
        string absolutePath,
        string relativePath,
        string name,
        long size,
        int maxDepth,
        CancellationToken cancellationToken = default)
    {
        var node = new ScanNode
        {
            Name = name,
            RelativePath = relativePath,
            Type = NodeType.Archive,
            Size = size,
            AbsolutePath = absolutePath,
            Children = new Dictionary<string, ScanNode>(_childComparer)
        };

        if (maxDepth <= 0)
        {
            return node;
        }

        if (size > _options.MaxArchiveSizeBytes)
        {
            node.Error = $"The archive is larger than the configured maximum of {_options.MaxArchiveSizeBytes} bytes and was not inspected.";
            return node;
        }

        var scanner = FindScanner(name);
        if (scanner is null)
        {
            node.Error = "No scanner is registered for this archive format.";
            return node;
        }

        try
        {
            await using var stream = new FileStream(
                absolutePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 64 * 1024,
                FileOptions.Asynchronous);

            await PopulateAsync(node, scanner, stream, new List<string>(), maxDepth, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ComparisonException ex)
        {
            _logger.LogWarning("Archive {Path} could not be inspected: {Message}", relativePath, ex.Message);
            node.Error = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected failure while inspecting archive {Path}", relativePath);
            node.Error = $"The archive could not be inspected: {ex.Message}";
        }

        return node;
    }

    public async Task<Stream> ReadEntryContentAsync(ArchiveEntryLocation location, long maxBytes, CancellationToken cancellationToken = default)
    {
        if (location.EntrySegments.Count == 0)
        {
            throw new ComparisonException(ErrorCodes.NodeNotFound, "No archive entry was specified.", StatusCodes.Status404NotFound);
        }

        Stream current = new FileStream(
            location.ArchiveFilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous);

        var containerName = Path.GetFileName(location.ArchiveFilePath);

        try
        {
            for (var i = 0; i < location.EntrySegments.Count; i++)
            {
                var scanner = FindScanner(containerName)
                              ?? throw new ComparisonException(
                                  ErrorCodes.ArchiveUnsupported,
                                  $"No scanner is registered for '{containerName}'.",
                                  StatusCodes.Status422UnprocessableEntity);

                var isLast = i == location.EntrySegments.Count - 1;
                var limit = isLast ? maxBytes : _options.MaxNestedArchiveBufferBytes;

                var next = await scanner.ReadEntryAsync(current, location.EntrySegments[i], limit, cancellationToken).ConfigureAwait(false);
                await current.DisposeAsync().ConfigureAwait(false);
                current = next;
                containerName = location.EntrySegments[i];
            }

            return current;
        }
        catch
        {
            await current.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task PopulateAsync(
        ScanNode archiveNode,
        IArchiveScanner scanner,
        Stream archiveStream,
        List<string> parentSegments,
        int remainingDepth,
        CancellationToken cancellationToken)
    {
        var entries = await scanner.ScanAsync(archiveStream, cancellationToken).ConfigureAwait(false);
        var nestedCandidates = new List<(ScanNode Node, string EntryPath)>();

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var segments = VirtualPath.SplitEntryPath(entry.Path);
            if (segments.Length == 0)
            {
                continue;
            }

            var parent = archiveNode;
            var walkedPath = string.Empty;

            for (var i = 0; i < segments.Length - 1; i++)
            {
                walkedPath = walkedPath.Length == 0 ? segments[i] : walkedPath + "/" + segments[i];
                parent = GetOrCreateFolder(parent, segments[i], VirtualPath.CombineArchive(archiveNode.RelativePath, walkedPath));
            }

            var leafName = segments[^1];
            var leafEntryPath = string.Join('/', segments);
            var leafVirtualPath = VirtualPath.CombineArchive(archiveNode.RelativePath, leafEntryPath);

            if (entry.IsDirectory)
            {
                GetOrCreateFolder(parent, leafName, leafVirtualPath);
                continue;
            }

            parent.Children ??= new Dictionary<string, ScanNode>(_childComparer);

            var entrySegments = new List<string>(parentSegments) { leafEntryPath };
            var isOversized = entry.Size.HasValue && entry.Size.Value > _options.MaxArchiveEntrySizeBytes;
            var expandsAsArchive = !isOversized && remainingDepth > 1 && IsArchive(leafName);

            var fileNode = new ScanNode
            {
                Name = leafName,
                RelativePath = leafVirtualPath,
                Type = expandsAsArchive ? NodeType.Archive : NodeType.ArchiveFile,
                Size = entry.Size,
                Crc32 = entry.Crc32,
                ArchiveLocation = new ArchiveEntryLocation(GetArchiveFilePath(archiveNode), entrySegments),
                Children = expandsAsArchive ? new Dictionary<string, ScanNode>(_childComparer) : null,
                Error = isOversized
                    ? $"The entry is larger than the configured maximum entry size of {_options.MaxArchiveEntrySizeBytes} bytes."
                    : null
            };

            parent.Children[fileNode.Name] = fileNode;

            if (expandsAsArchive)
            {
                nestedCandidates.Add((fileNode, leafEntryPath));
            }
        }

        foreach (var (node, entryPath) in nestedCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ExpandNestedArchiveAsync(node, scanner, archiveStream, entryPath, parentSegments, remainingDepth, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Expands an archive stored inside another archive. The nested archive is buffered in
    /// memory because random access is required to read its directory, so the configured
    /// buffer limit is what keeps nesting affordable.
    /// </summary>
    private async Task ExpandNestedArchiveAsync(
        ScanNode node,
        IArchiveScanner outerScanner,
        Stream outerStream,
        string entryPath,
        List<string> parentSegments,
        int remainingDepth,
        CancellationToken cancellationToken)
    {
        if (node.Size > _options.MaxNestedArchiveBufferBytes)
        {
            node.Error = $"The nested archive is larger than the {_options.MaxNestedArchiveBufferBytes} byte buffer limit and was not inspected.";
            return;
        }

        var nestedScanner = FindScanner(node.Name);
        if (nestedScanner is null)
        {
            return;
        }

        try
        {
            await using var nestedStream = await outerScanner
                .ReadEntryAsync(outerStream, entryPath, _options.MaxNestedArchiveBufferBytes, cancellationToken)
                .ConfigureAwait(false);

            var nestedSegments = new List<string>(parentSegments) { entryPath };
            await PopulateAsync(node, nestedScanner, nestedStream, nestedSegments, remainingDepth - 1, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ComparisonException ex)
        {
            node.Error = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Nested archive {Path} could not be inspected", node.RelativePath);
            node.Error = $"The nested archive could not be inspected: {ex.Message}";
        }
    }

    private static string GetArchiveFilePath(ScanNode archiveNode)
        => archiveNode.AbsolutePath ?? archiveNode.ArchiveLocation?.ArchiveFilePath
           ?? throw new InvalidOperationException("Archive node has no backing file.");

    private ScanNode GetOrCreateFolder(ScanNode parent, string name, string relativePath)
    {
        parent.Children ??= new Dictionary<string, ScanNode>(_childComparer);

        if (parent.Children.TryGetValue(name, out var existing) && existing.IsContainer)
        {
            return existing;
        }

        var folder = new ScanNode
        {
            Name = name,
            RelativePath = relativePath,
            Type = NodeType.ArchiveFolder,
            Children = new Dictionary<string, ScanNode>(_childComparer)
        };

        parent.Children[name] = folder;
        return folder;
    }

    private IArchiveScanner? FindScanner(string fileName) => _scanners.FirstOrDefault(scanner => scanner.CanHandle(fileName));
}
