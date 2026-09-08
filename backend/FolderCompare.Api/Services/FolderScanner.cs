using FolderCompare.Api.Models;
using FolderCompare.Api.Utilities;

namespace FolderCompare.Api.Services;

/// <inheritdoc />
public sealed class FolderScanner : IFolderScanner
{
    /// <summary>Guards against pathological trees and reparse loops.</summary>
    private const int MaxDepth = 512;

    private readonly IArchiveTreeService _archiveTreeService;
    private readonly ILogger<FolderScanner> _logger;

    public FolderScanner(IArchiveTreeService archiveTreeService, ILogger<FolderScanner> logger)
    {
        _archiveTreeService = archiveTreeService;
        _logger = logger;
    }

    public async Task<FolderSnapshot> ScanAsync(
        string rootPath,
        ScanSettings settings,
        IProgress<ScanProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var comparer = PathHelper.GetComparer(settings.CaseSensitive);

        var root = new ScanNode
        {
            Name = "/",
            RelativePath = "/",
            Type = NodeType.Folder,
            AbsolutePath = rootPath,
            Children = new Dictionary<string, ScanNode>(comparer)
        };

        var snapshot = new FolderSnapshot { RootPath = rootPath, Root = root };

        await ScanFolderAsync(new DirectoryInfo(rootPath), root, snapshot, settings, comparer, 0, progress, cancellationToken)
            .ConfigureAwait(false);

        return snapshot;
    }

    private async Task ScanFolderAsync(
        DirectoryInfo directory,
        ScanNode node,
        FolderSnapshot snapshot,
        ScanSettings settings,
        StringComparer comparer,
        int depth,
        IProgress<ScanProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        if (depth >= MaxDepth)
        {
            node.Error = "Maximum folder depth reached; deeper entries were not scanned.";
            snapshot.Warnings.Add($"{node.RelativePath}: maximum folder depth reached.");
            return;
        }

        IEnumerable<FileSystemInfo> entries;
        try
        {
            entries = directory.EnumerateFileSystemInfos();
        }
        catch (UnauthorizedAccessException ex)
        {
            node.Error = "Access denied.";
            snapshot.Warnings.Add($"{node.RelativePath}: access denied.");
            _logger.LogWarning("Access denied while scanning {Path}: {Message}", directory.FullName, ex.Message);
            return;
        }
        catch (DirectoryNotFoundException)
        {
            node.Error = "The folder disappeared during the scan.";
            snapshot.Warnings.Add($"{node.RelativePath}: the folder disappeared during the scan.");
            return;
        }

        using var enumerator = entries.GetEnumerator();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            FileSystemInfo entry;
            try
            {
                if (!enumerator.MoveNext())
                {
                    break;
                }

                entry = enumerator.Current;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                snapshot.Warnings.Add($"{node.RelativePath}: {ex.Message}");
                break;
            }

            if (!settings.FollowSymlinks && entry.LinkTarget is not null)
            {
                snapshot.Warnings.Add($"{VirtualPath.Combine(node.RelativePath, entry.Name)}: symbolic links are not followed.");
                continue;
            }

            var childPath = VirtualPath.Combine(node.RelativePath, entry.Name);
            node.Children ??= new Dictionary<string, ScanNode>(comparer);

            if (entry is DirectoryInfo subDirectory)
            {
                var folderNode = new ScanNode
                {
                    Name = entry.Name,
                    RelativePath = childPath,
                    Type = NodeType.Folder,
                    AbsolutePath = subDirectory.FullName,
                    Children = new Dictionary<string, ScanNode>(comparer)
                };

                node.Children[folderNode.Name] = folderNode;
                snapshot.FolderCount++;
                progress?.Report(new ScanProgressUpdate(NodeType.Folder, childPath));

                await ScanFolderAsync(subDirectory, folderNode, snapshot, settings, comparer, depth + 1, progress, cancellationToken)
                    .ConfigureAwait(false);

                continue;
            }

            if (entry is not FileInfo file)
            {
                continue;
            }

            long size;
            try
            {
                size = file.Length;
            }
            catch (FileNotFoundException)
            {
                snapshot.Warnings.Add($"{childPath}: the file disappeared during the scan.");
                continue;
            }
            catch (IOException ex)
            {
                snapshot.Warnings.Add($"{childPath}: {ex.Message}");
                continue;
            }

            if (settings.ArchiveMaxDepth > 0 && _archiveTreeService.IsArchive(file.Name))
            {
                var archiveNode = await _archiveTreeService
                    .BuildArchiveNodeAsync(file.FullName, childPath, file.Name, size, settings.ArchiveMaxDepth, cancellationToken)
                    .ConfigureAwait(false);

                node.Children[archiveNode.Name] = archiveNode;
                snapshot.ArchiveCount++;
                snapshot.FileCount++;

                if (archiveNode.Error is not null)
                {
                    snapshot.Warnings.Add($"{childPath}: {archiveNode.Error}");
                }

                progress?.Report(new ScanProgressUpdate(NodeType.Archive, childPath));
                continue;
            }

            node.Children[file.Name] = new ScanNode
            {
                Name = file.Name,
                RelativePath = childPath,
                Type = NodeType.File,
                Size = size,
                AbsolutePath = file.FullName
            };

            snapshot.FileCount++;
            progress?.Report(new ScanProgressUpdate(NodeType.File, childPath));
        }
    }
}
