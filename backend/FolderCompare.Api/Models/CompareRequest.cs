using System.ComponentModel.DataAnnotations;

namespace FolderCompare.Api.Models;

public sealed class CompareRequest
{
    [Required(AllowEmptyStrings = false, ErrorMessage = "leftPath is required.")]
    public string LeftPath { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false, ErrorMessage = "rightPath is required.")]
    public string RightPath { get; set; } = string.Empty;

    /// <summary>
    /// Overrides <c>Comparison:CalculateHashes</c> for this request. When hashes are disabled,
    /// same-size files are reported as unchanged without reading their contents.
    /// </summary>
    public bool? CalculateHashes { get; set; }

    /// <summary>Overrides <c>Comparison:ArchiveMaxDepth</c> for this request.</summary>
    public int? ArchiveMaxDepth { get; set; }
}
