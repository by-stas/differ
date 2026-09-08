using FolderCompare.Api.Configuration;
using FolderCompare.Api.Models;
using FolderCompare.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FolderCompare.Tests.TestSupport;

/// <summary>
/// Wires the real services together without a host so unit tests exercise the same code paths
/// the API uses.
/// </summary>
public sealed class ComparisonHarness
{
    public ComparisonHarness(Action<ComparisonOptions>? configure = null)
    {
        Options = new ComparisonOptions();
        configure?.Invoke(Options);

        var optionsWrapper = Microsoft.Extensions.Options.Options.Create(Options);

        ArchiveScanner = new ZipArchiveScanner(optionsWrapper, NullLogger<ZipArchiveScanner>.Instance);
        ArchiveTreeService = new ArchiveTreeService(new[] { ArchiveScanner }, optionsWrapper, NullLogger<ArchiveTreeService>.Instance);
        FolderScanner = new FolderScanner(ArchiveTreeService, NullLogger<FolderScanner>.Instance);
        FileComparisonService = new FileComparisonService(ArchiveTreeService, optionsWrapper, NullLogger<FileComparisonService>.Instance);
        TreeBuilder = new ComparisonTreeBuilder(FileComparisonService, optionsWrapper);
        Store = new InMemoryComparisonStore(optionsWrapper, NullLogger<InMemoryComparisonStore>.Instance);
        FileContentService = new FileContentService(ArchiveTreeService, optionsWrapper, NullLogger<FileContentService>.Instance);
        ComparisonService = new ComparisonService(FolderScanner, TreeBuilder, Store, optionsWrapper, NullLogger<ComparisonService>.Instance);
    }

    public ComparisonOptions Options { get; }

    public IArchiveScanner ArchiveScanner { get; }

    public IArchiveTreeService ArchiveTreeService { get; }

    public IFolderScanner FolderScanner { get; }

    public IFileComparisonService FileComparisonService { get; }

    public IComparisonTreeBuilder TreeBuilder { get; }

    public IComparisonStore Store { get; }

    public IFileContentService FileContentService { get; }

    public IComparisonService ComparisonService { get; }

    /// <summary>Runs a full comparison and waits for it to finish.</summary>
    public async Task<ComparisonResult> CompareAsync(string leftPath, string rightPath)
    {
        var result = await ComparisonService.StartAsync(new CompareRequest { LeftPath = leftPath, RightPath = rightPath });
        await WaitForCompletionAsync(result.Id);
        return ComparisonService.GetResult(result.Id)!;
    }

    public async Task<ComparisonJob> CompareJobAsync(string leftPath, string rightPath)
    {
        var result = await ComparisonService.StartAsync(new CompareRequest { LeftPath = leftPath, RightPath = rightPath });
        await WaitForCompletionAsync(result.Id);
        return ComparisonService.GetJob(result.Id)!;
    }

    private async Task WaitForCompletionAsync(string comparisonId)
    {
        var job = ComparisonService.GetJob(comparisonId);
        if (job is not null)
        {
            await job.Execution.WaitAsync(TimeSpan.FromMinutes(2));
        }
    }
}

public static class ComparisonNodeExtensions
{
    /// <summary>Finds a node by its normalized virtual path.</summary>
    public static ComparisonNode? Find(this ComparisonNode? root, string relativePath)
    {
        if (root is null)
        {
            return null;
        }

        if (string.Equals(root.RelativePath, relativePath, StringComparison.Ordinal))
        {
            return root;
        }

        if (root.Children is null)
        {
            return null;
        }

        foreach (var child in root.Children)
        {
            var match = child.Find(relativePath);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    public static ComparisonNode Require(this ComparisonNode? root, string relativePath)
        => root.Find(relativePath) ?? throw new Xunit.Sdk.XunitException($"Node '{relativePath}' was not found in the comparison tree.");

    public static IEnumerable<ComparisonNode> Flatten(this ComparisonNode? root)
    {
        if (root is null)
        {
            yield break;
        }

        yield return root;

        if (root.Children is null)
        {
            yield break;
        }

        foreach (var descendant in root.Children.SelectMany(child => child.Flatten()))
        {
            yield return descendant;
        }
    }
}
