using System.Collections.Concurrent;
using System.Diagnostics;
using FolderCompare.Api.Configuration;
using FolderCompare.Api.Infrastructure;
using FolderCompare.Api.Models;
using FolderCompare.Api.Utilities;
using Microsoft.Extensions.Options;

namespace FolderCompare.Api.Services;

/// <inheritdoc />
public sealed class ComparisonService : IComparisonService
{
    private readonly IFolderScanner _folderScanner;
    private readonly IComparisonTreeBuilder _treeBuilder;
    private readonly IComparisonStore _store;
    private readonly ComparisonOptions _options;
    private readonly ILogger<ComparisonService> _logger;

    public ComparisonService(
        IFolderScanner folderScanner,
        IComparisonTreeBuilder treeBuilder,
        IComparisonStore store,
        IOptions<ComparisonOptions> options,
        ILogger<ComparisonService> logger)
    {
        _folderScanner = folderScanner;
        _treeBuilder = treeBuilder;
        _store = store;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ComparisonResult> StartAsync(CompareRequest request, CancellationToken cancellationToken = default)
    {
        var leftRoot = PathHelper.ValidateRootFolder(request.LeftPath, "left", _options);
        var rightRoot = PathHelper.ValidateRootFolder(request.RightPath, "right", _options);

        var caseSensitive = PathHelper.ResolveCaseSensitivity(_options);
        var archiveDepth = Math.Max(0, request.ArchiveMaxDepth ?? _options.ArchiveMaxDepth);
        var calculateHashes = request.CalculateHashes ?? _options.CalculateHashes;

        var id = "cmp-" + Guid.NewGuid().ToString("N")[..12];

        var job = new ComparisonJob
        {
            Id = id,
            LeftRoot = leftRoot,
            RightRoot = rightRoot,
            Settings = new CompareSettings(caseSensitive, calculateHashes),
            ScanSettings = new ScanSettings(archiveDepth, caseSensitive, _options.FollowSymlinks),
            NodeIndex = new ConcurrentDictionary<string, ComparisonNode>(PathHelper.GetComparer(caseSensitive)),
            Result = new ComparisonResult
            {
                Id = id,
                LeftPath = leftRoot,
                RightPath = rightRoot,
                Status = ComparisonState.Pending
            }
        };

        _store.Add(job);
        job.Execution = Task.Run(() => RunAsync(job), CancellationToken.None);

        var waitMs = Math.Max(0, _options.SynchronousWaitMs);
        if (waitMs > 0)
        {
            var completed = await Task.WhenAny(job.Execution, Task.Delay(waitMs, cancellationToken)).ConfigureAwait(false);
            if (completed != job.Execution)
            {
                _logger.LogInformation(
                    "Comparison {ComparisonId} is still running after {WaitMs} ms; returning a pollable job",
                    job.Id,
                    waitMs);
            }
        }

        return job.Result;
    }

    public ComparisonResult? GetResult(string comparisonId) => _store.Get(comparisonId)?.Result;

    public ComparisonJob? GetJob(string comparisonId) => _store.Get(comparisonId);

    public bool Cancel(string comparisonId)
    {
        var job = _store.Get(comparisonId);
        if (job is null)
        {
            return false;
        }

        job.Cancel();
        return true;
    }

    private async Task RunAsync(ComparisonJob job)
    {
        var result = job.Result;
        var stopwatch = Stopwatch.StartNew();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(job.CancellationToken);
        timeout.CancelAfter(_options.ComparisonTimeout);
        var cancellationToken = timeout.Token;

        _logger.LogInformation(
            "Comparison {ComparisonId} started for left={LeftPath} right={RightPath} (archiveDepth={ArchiveDepth}, hashes={Hashes})",
            job.Id,
            job.LeftRoot,
            job.RightRoot,
            job.ScanSettings.ArchiveMaxDepth,
            job.Settings.CalculateHashes);

        try
        {
            result.Status = ComparisonState.Running;
            result.Progress.Phase = "Scanning";

            var progress = new ScanProgressCollector(result.Progress);

            var leftTask = _folderScanner.ScanAsync(job.LeftRoot, job.ScanSettings, progress, cancellationToken);
            var rightTask = _folderScanner.ScanAsync(job.RightRoot, job.ScanSettings, progress, cancellationToken);

            await Task.WhenAll(leftTask, rightTask).ConfigureAwait(false);

            var left = await leftTask.ConfigureAwait(false);
            var right = await rightTask.ConfigureAwait(false);

            result.Warnings.AddRange(left.Warnings.Select(warning => $"left: {warning}"));
            result.Warnings.AddRange(right.Warnings.Select(warning => $"right: {warning}"));

            result.Progress.Phase = "Comparing";

            var summary = new ComparisonSummary();
            var root = await _treeBuilder.BuildAsync(
                    left.Root,
                    right.Root,
                    job.Settings,
                    summary,
                    node =>
                    {
                        job.NodeIndex[node.RelativePath] = node;
                        result.Progress.ComparedNodes++;
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            result.Root = root;
            result.Summary = summary;
            result.Progress.Phase = "Completed";
            result.Progress.CurrentPath = null;
            result.Status = ComparisonState.Completed;

            _logger.LogInformation(
                "Comparison {ComparisonId} completed in {DurationMs} ms: {Files} files, {Folders} folders, {Archives} archives scanned; " +
                "added={Added}, removed={Removed}, modified={Modified}, unchanged={Unchanged}",
                job.Id,
                stopwatch.ElapsedMilliseconds,
                left.FileCount + right.FileCount,
                left.FolderCount + right.FolderCount,
                left.ArchiveCount + right.ArchiveCount,
                summary.Added,
                summary.Removed,
                summary.Modified,
                summary.Unchanged);
        }
        catch (OperationCanceledException)
        {
            result.Status = ComparisonState.Cancelled;
            result.Progress.Phase = "Cancelled";
            result.Error = new ApiError(ErrorCodes.ComparisonCancelled, "The comparison was cancelled.");
            _logger.LogInformation("Comparison {ComparisonId} was cancelled after {DurationMs} ms", job.Id, stopwatch.ElapsedMilliseconds);
        }
        catch (ComparisonException ex)
        {
            result.Status = ComparisonState.Failed;
            result.Progress.Phase = "Failed";
            result.Error = ex.ToApiError();
            _logger.LogError(ex, "Comparison {ComparisonId} failed: {Message}", job.Id, ex.Message);
        }
        catch (Exception ex)
        {
            result.Status = ComparisonState.Failed;
            result.Progress.Phase = "Failed";
            result.Error = new ApiError(ErrorCodes.InternalError, "The comparison failed unexpectedly.", ex.Message);
            _logger.LogError(ex, "Comparison {ComparisonId} failed unexpectedly", job.Id);
        }
        finally
        {
            stopwatch.Stop();
            result.DurationMs = stopwatch.ElapsedMilliseconds;
            result.CompletedAt = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>
    /// Aggregates scanner callbacks into the progress object the API exposes. Both roots are
    /// scanned concurrently, so the counters are guarded by a lock.
    /// </summary>
    private sealed class ScanProgressCollector : IProgress<ScanProgressUpdate>
    {
        private readonly ComparisonProgress _progress;
        private readonly object _gate = new();

        public ScanProgressCollector(ComparisonProgress progress)
        {
            _progress = progress;
        }

        public void Report(ScanProgressUpdate value)
        {
            lock (_gate)
            {
                switch (value.Kind)
                {
                    case NodeType.Folder:
                        _progress.ScannedFolders++;
                        break;
                    case NodeType.Archive:
                        _progress.ScannedArchives++;
                        _progress.ScannedFiles++;
                        break;
                    default:
                        _progress.ScannedFiles++;
                        break;
                }

                _progress.CurrentPath = value.RelativePath;
            }
        }
    }
}
