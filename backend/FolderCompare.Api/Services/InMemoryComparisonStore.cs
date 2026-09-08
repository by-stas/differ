using System.Collections.Concurrent;
using FolderCompare.Api.Configuration;
using Microsoft.Extensions.Options;

namespace FolderCompare.Api.Services;

/// <inheritdoc />
public sealed class InMemoryComparisonStore : IComparisonStore
{
    private readonly ConcurrentDictionary<string, ComparisonJob> _jobs = new(StringComparer.Ordinal);
    private readonly ComparisonOptions _options;
    private readonly ILogger<InMemoryComparisonStore> _logger;

    public InMemoryComparisonStore(IOptions<ComparisonOptions> options, ILogger<InMemoryComparisonStore> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public void Add(ComparisonJob job)
    {
        _jobs[job.Id] = job;
        Evict();
    }

    public ComparisonJob? Get(string id)
    {
        if (!_jobs.TryGetValue(id, out var job))
        {
            return null;
        }

        job.LastAccessedAt = DateTimeOffset.UtcNow;
        return job;
    }

    public bool Remove(string id)
    {
        if (!_jobs.TryRemove(id, out var job))
        {
            return false;
        }

        job.Dispose();
        return true;
    }

    /// <summary>Drops expired results first, then the oldest ones while the store is over capacity.</summary>
    private void Evict()
    {
        var cutoff = DateTimeOffset.UtcNow - _options.ResultRetention;

        foreach (var expired in _jobs.Values.Where(job => job.LastAccessedAt < cutoff).ToList())
        {
            if (_jobs.TryRemove(expired.Id, out var removed))
            {
                _logger.LogDebug("Evicted expired comparison {ComparisonId}", removed.Id);
                removed.Dispose();
            }
        }

        var overflow = _jobs.Count - _options.MaxStoredComparisons;
        if (overflow <= 0)
        {
            return;
        }

        foreach (var oldest in _jobs.Values.OrderBy(job => job.LastAccessedAt).Take(overflow).ToList())
        {
            if (_jobs.TryRemove(oldest.Id, out var removed))
            {
                _logger.LogDebug("Evicted comparison {ComparisonId} to stay within the configured capacity", removed.Id);
                removed.Dispose();
            }
        }
    }
}
