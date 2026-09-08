using FolderCompare.Api.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace FolderCompare.Api.Controllers;

[ApiController]
[Route("api/health")]
[Produces("application/json")]
public sealed class HealthController : ControllerBase
{
    private readonly ComparisonOptions _options;

    public HealthController(IOptions<ComparisonOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>Liveness probe plus the limits the frontend needs to explain itself to users.</summary>
    [HttpGet]
    public IActionResult Get() => Ok(new
    {
        status = "ok",
        version = typeof(HealthController).Assembly.GetName().Version?.ToString(),
        limits = new
        {
            maxDiffFileSizeBytes = _options.MaxDiffFileSizeBytes,
            archiveMaxDepth = _options.ArchiveMaxDepth,
            maxArchiveEntries = _options.MaxArchiveEntries,
            caseSensitive = Utilities.PathHelper.ResolveCaseSensitivity(_options),
            restrictedToAllowedRoots = _options.AllowedRoots.Count > 0
        }
    });
}
