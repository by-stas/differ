using System.Collections.Concurrent;
using FolderCompare.Api.Models;

namespace FolderCompare.Api.Services;

/// <summary>
/// A comparison and everything needed to answer follow-up requests for it: the result itself,
/// the validated roots and an index of nodes by virtual path.
/// </summary>
public sealed class ComparisonJob : IDisposable
{
    private readonly CancellationTokenSource _cancellation = new();

    public required string Id { get; init; }

    public required string LeftRoot { get; init; }

    public required string RightRoot { get; init; }

    public required ComparisonResult Result { get; init; }

    public required CompareSettings Settings { get; init; }

    public required ScanSettings ScanSettings { get; init; }

    public ConcurrentDictionary<string, ComparisonNode> NodeIndex { get; init; } = new(StringComparer.Ordinal);

    public Task Execution { get; set; } = Task.CompletedTask;

    public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;

    public DateTimeOffset LastAccessedAt { get; set; } = DateTimeOffset.UtcNow;

    public CancellationToken CancellationToken => _cancellation.Token;

    public void Cancel()
    {
        if (!_cancellation.IsCancellationRequested)
        {
            _cancellation.Cancel();
        }
    }

    public void Dispose()
    {
        try
        {
            _cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already disposed; nothing to cancel.
        }

        _cancellation.Dispose();
    }
}
