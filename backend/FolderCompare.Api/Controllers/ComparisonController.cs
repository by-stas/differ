using FolderCompare.Api.Models;
using FolderCompare.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace FolderCompare.Api.Controllers;

[ApiController]
[Route("api/comparisons")]
[Produces("application/json")]
public sealed class ComparisonController : ControllerBase
{
    private readonly IComparisonService _comparisonService;
    private readonly ILogger<ComparisonController> _logger;

    public ComparisonController(IComparisonService comparisonService, ILogger<ComparisonController> logger)
    {
        _comparisonService = comparisonService;
        _logger = logger;
    }

    /// <summary>
    /// Starts a comparison. Short comparisons are returned completed; longer ones come back
    /// with status <c>running</c> and are polled through <see cref="GetAsync"/>.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ComparisonResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ComparisonResult), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> StartAsync([FromBody] CompareRequest request, CancellationToken cancellationToken)
    {
        var result = await _comparisonService.StartAsync(request, cancellationToken);

        return result.Status switch
        {
            ComparisonState.Completed or ComparisonState.Failed or ComparisonState.Cancelled => Ok(result),
            _ => Accepted($"/api/comparisons/{result.Id}", result)
        };
    }

    /// <summary>Returns the current state, progress and (once available) the comparison tree.</summary>
    [HttpGet("{comparisonId}")]
    [ProducesResponseType(typeof(ComparisonResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public IActionResult GetAsync(string comparisonId)
    {
        var result = _comparisonService.GetResult(comparisonId);
        if (result is null)
        {
            return NotFound(new ApiError(ErrorCodes.ComparisonNotFound, $"Comparison '{comparisonId}' was not found or has expired."));
        }

        return Ok(result);
    }

    /// <summary>Cancels a running comparison.</summary>
    [HttpDelete("{comparisonId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public IActionResult Cancel(string comparisonId)
    {
        if (!_comparisonService.Cancel(comparisonId))
        {
            return NotFound(new ApiError(ErrorCodes.ComparisonNotFound, $"Comparison '{comparisonId}' was not found or has expired."));
        }

        _logger.LogInformation("Cancellation requested for comparison {ComparisonId}", comparisonId);
        return NoContent();
    }
}
