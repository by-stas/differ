using FolderCompare.Api.Models;

namespace FolderCompare.Api.Infrastructure;

/// <summary>
/// Exception carrying an API error code so controllers can translate failures into the
/// structured error contract without inspecting exception types.
/// </summary>
public class ComparisonException : Exception
{
    public ComparisonException(string code, string message, int statusCode = 400, string? detail = null, Exception? inner = null)
        : base(message, inner)
    {
        Code = code;
        StatusCode = statusCode;
        Detail = detail;
    }

    public string Code { get; }

    public int StatusCode { get; }

    public string? Detail { get; }

    public ApiError ToApiError() => new(Code, Message, Detail);
}

/// <summary>Raised when an archive violates one of the configured safety limits.</summary>
public sealed class ArchiveLimitException : ComparisonException
{
    public ArchiveLimitException(string message)
        : base(ErrorCodes.ArchiveLimitExceeded, message, StatusCodes.Status422UnprocessableEntity)
    {
    }
}
