using FolderCompare.Api.Models;

namespace FolderCompare.Api.Services;

/// <summary>
/// Loads the two sides of a file for the diff editor. Works for files on disk and for entries
/// inside archives, so the frontend never needs to know where a file actually lives.
/// </summary>
public interface IFileContentService
{
    Task<FileContentResult> GetContentAsync(ComparisonJob job, string relativePath, CancellationToken cancellationToken = default);
}
