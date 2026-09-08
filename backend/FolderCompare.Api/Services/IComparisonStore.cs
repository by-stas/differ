namespace FolderCompare.Api.Services;

/// <summary>
/// Keeps comparison results available for follow-up requests. The MVP stores them in memory,
/// bounded by count and age.
/// </summary>
public interface IComparisonStore
{
    void Add(ComparisonJob job);

    ComparisonJob? Get(string id);

    bool Remove(string id);
}
