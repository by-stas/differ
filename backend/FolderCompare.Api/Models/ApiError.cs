namespace FolderCompare.Api.Models;

/// <summary>Structured error payload returned by every failing endpoint.</summary>
public sealed class ApiError
{
    public ApiError()
    {
    }

    public ApiError(string code, string message, string? detail = null)
    {
        Code = code;
        Message = message;
        Detail = detail;
    }

    public string Code { get; set; } = ErrorCodes.InternalError;

    public string Message { get; set; } = string.Empty;

    public string? Detail { get; set; }
}

public static class ErrorCodes
{
    public const string PathNotFound = "PATH_NOT_FOUND";
    public const string PathInvalid = "PATH_INVALID";
    public const string PathNotAllowed = "PATH_NOT_ALLOWED";
    public const string AccessDenied = "ACCESS_DENIED";
    public const string ValidationFailed = "VALIDATION_FAILED";
    public const string ComparisonNotFound = "COMPARISON_NOT_FOUND";
    public const string ComparisonNotCompleted = "COMPARISON_NOT_COMPLETED";
    public const string ComparisonCancelled = "COMPARISON_CANCELLED";
    public const string NodeNotFound = "NODE_NOT_FOUND";
    public const string ContentNotComparable = "CONTENT_NOT_COMPARABLE";
    public const string FileTooLarge = "FILE_TOO_LARGE";
    public const string FileNotText = "FILE_NOT_TEXT";
    public const string EncodingNotDetected = "ENCODING_NOT_DETECTED";
    public const string ArchiveCorrupted = "ARCHIVE_CORRUPTED";
    public const string ArchiveUnsupported = "ARCHIVE_UNSUPPORTED";
    public const string ArchiveLimitExceeded = "ARCHIVE_LIMIT_EXCEEDED";
    public const string FileDisappeared = "FILE_DISAPPEARED";
    public const string InternalError = "INTERNAL_ERROR";
}
