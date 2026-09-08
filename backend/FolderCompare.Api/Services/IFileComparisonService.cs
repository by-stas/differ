using FolderCompare.Api.Models;

namespace FolderCompare.Api.Services;

/// <param name="CanCompare">True when the node may be opened in the diff editor.</param>
/// <param name="Reason">Explanation shown to the user when <paramref name="CanCompare"/> is false.</param>
public readonly record struct ContentEligibility(bool CanCompare, string? Reason);

/// <summary>Decides whether two leaf nodes have the same content and whether they can be diffed.</summary>
public interface IFileComparisonService
{
    /// <summary>
    /// Compares two leaf nodes following the cheap-to-expensive strategy: size first, then a
    /// checksum, and only then the actual bytes.
    /// </summary>
    Task<bool> AreEqualAsync(ScanNode left, ScanNode right, bool calculateHashes, CancellationToken cancellationToken = default);

    /// <summary>Applies the text/size rules that decide whether the diff editor can be opened.</summary>
    ContentEligibility EvaluateEligibility(ScanNode? left, ScanNode? right, ComparisonStatus status);
}
