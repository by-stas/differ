using FolderCompare.Api.Models;
using FolderCompare.Api.Services;
using Microsoft.AspNetCore.Mvc;

// Disambiguates from Microsoft.AspNetCore.Mvc.FileContentResult.
using FileDiffContent = FolderCompare.Api.Models.FileContentResult;

namespace FolderCompare.Api.Controllers;

[ApiController]
[Route("api/comparisons/{comparisonId}/content")]
[Produces("application/json")]
public sealed class FileContentController : ControllerBase
{
    private readonly IComparisonService _comparisonService;
    private readonly IFileContentService _fileContentService;

    public FileContentController(IComparisonService comparisonService, IFileContentService fileContentService)
    {
        _comparisonService = comparisonService;
        _fileContentService = fileContentService;
    }

    /// <summary>
    /// Returns both sides of a file so they can be shown in the diff editor. The path may point
    /// at a file on disk (<c>/src/config.json</c>) or inside an archive
    /// (<c>/package.zip!/config/settings.json</c>).
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(FileDiffContent), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(string comparisonId, [FromQuery] string? path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return BadRequest(new ApiError(ErrorCodes.ValidationFailed, "The 'path' query parameter is required."));
        }

        var job = _comparisonService.GetJob(comparisonId);
        if (job is null)
        {
            return NotFound(new ApiError(ErrorCodes.ComparisonNotFound, $"Comparison '{comparisonId}' was not found or has expired."));
        }

        var content = await _fileContentService.GetContentAsync(job, path, cancellationToken);
        return Ok(content);
    }
}
