namespace FolderCompare.Api.Models;

/// <summary>Payload consumed by the Monaco diff editor.</summary>
public sealed class FileContentResult
{
    public required string RelativePath { get; init; }

    public string? LeftContent { get; init; }

    public string? RightContent { get; init; }

    /// <summary>Monaco language identifier, e.g. <c>json</c>.</summary>
    public string Language { get; init; } = "plaintext";

    public long? LeftSize { get; init; }

    public long? RightSize { get; init; }

    public string? LeftEncoding { get; init; }

    public string? RightEncoding { get; init; }

    public bool CanCompareContent { get; init; }

    public string? Reason { get; init; }
}
