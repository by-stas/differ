using FolderCompare.Api.Models;

namespace FolderCompare.Api.Services;

public interface IComparisonService
{
    /// <summary>
    /// Validates both roots, starts the comparison and waits briefly for it to finish. Short
    /// comparisons come back completed; longer ones come back running and are polled through
    /// <see cref="GetResult"/>.
    /// </summary>
    Task<ComparisonResult> StartAsync(CompareRequest request, CancellationToken cancellationToken = default);

    ComparisonResult? GetResult(string comparisonId);

    ComparisonJob? GetJob(string comparisonId);

    /// <summary>Requests cancellation of a running comparison.</summary>
    bool Cancel(string comparisonId);
}
