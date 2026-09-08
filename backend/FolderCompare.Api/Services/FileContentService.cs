using FolderCompare.Api.Configuration;
using FolderCompare.Api.Infrastructure;
using FolderCompare.Api.Models;
using FolderCompare.Api.Utilities;
using Microsoft.Extensions.Options;

namespace FolderCompare.Api.Services;

/// <inheritdoc />
public sealed class FileContentService : IFileContentService
{
    private readonly IArchiveTreeService _archiveTreeService;
    private readonly ComparisonOptions _options;
    private readonly ILogger<FileContentService> _logger;

    public FileContentService(
        IArchiveTreeService archiveTreeService,
        IOptions<ComparisonOptions> options,
        ILogger<FileContentService> logger)
    {
        _archiveTreeService = archiveTreeService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<FileContentResult> GetContentAsync(ComparisonJob job, string relativePath, CancellationToken cancellationToken = default)
    {
        if (job.Result.Status != ComparisonState.Completed)
        {
            throw new ComparisonException(
                ErrorCodes.ComparisonNotCompleted,
                "The comparison has not finished yet.",
                StatusCodes.Status409Conflict);
        }

        string normalized;
        try
        {
            normalized = VirtualPath.Normalize(relativePath);
        }
        catch (ArgumentException ex)
        {
            throw new ComparisonException(ErrorCodes.PathInvalid, "The requested path is not valid.", StatusCodes.Status400BadRequest, ex.Message);
        }

        if (!job.NodeIndex.TryGetValue(normalized, out var node))
        {
            throw new ComparisonException(
                ErrorCodes.NodeNotFound,
                $"'{normalized}' is not part of this comparison.",
                StatusCodes.Status404NotFound);
        }

        var name = node.Name;
        var language = FileTypeDetector.GetLanguage(name);

        if (!node.CanCompareContent)
        {
            return new FileContentResult
            {
                RelativePath = normalized,
                Language = language,
                LeftSize = node.LeftSize,
                RightSize = node.RightSize,
                CanCompareContent = false,
                Reason = node.ContentUnavailableReason ?? "This entry cannot be compared as text."
            };
        }

        var limit = _options.MaxDiffFileSizeBytes;
        var left = await LoadSideAsync(job.LeftRoot, normalized, limit, cancellationToken).ConfigureAwait(false);
        var right = await LoadSideAsync(job.RightRoot, normalized, limit, cancellationToken).ConfigureAwait(false);

        if (left.Bytes.Length > limit || right.Bytes.Length > limit)
        {
            return NotComparable(normalized, language, node, $"File exceeds the {limit} byte comparison limit.");
        }

        var leftDecoded = FileTypeDetector.Decode(left.Bytes);
        var rightDecoded = FileTypeDetector.Decode(right.Bytes);

        if (!leftDecoded.Success || !rightDecoded.Success)
        {
            _logger.LogInformation("Refusing to diff {Path}: binary content detected", normalized);
            return NotComparable(normalized, language, node, "The file contains binary data and cannot be shown as text.");
        }

        return new FileContentResult
        {
            RelativePath = normalized,
            LeftContent = leftDecoded.Text,
            RightContent = rightDecoded.Text,
            Language = language,
            LeftSize = left.Bytes.LongLength,
            RightSize = right.Bytes.LongLength,
            LeftEncoding = leftDecoded.EncodingName,
            RightEncoding = rightDecoded.EncodingName,
            CanCompareContent = true,
            Reason = leftDecoded.Reason ?? rightDecoded.Reason
        };
    }

    private static FileContentResult NotComparable(string path, string language, ComparisonNode node, string reason) => new()
    {
        RelativePath = path,
        Language = language,
        LeftSize = node.LeftSize,
        RightSize = node.RightSize,
        CanCompareContent = false,
        Reason = reason
    };

    private async Task<(byte[] Bytes, string Source)> LoadSideAsync(string root, string virtualPath, long limit, CancellationToken cancellationToken)
    {
        var (diskPath, archiveSegments) = VirtualPath.Split(virtualPath);
        var absolutePath = PathHelper.ResolveInsideRoot(root, diskPath);

        if (archiveSegments.Count == 0)
        {
            return (await ReadFileAsync(absolutePath, limit, cancellationToken).ConfigureAwait(false), absolutePath);
        }

        if (!File.Exists(absolutePath))
        {
            throw new ComparisonException(
                ErrorCodes.FileDisappeared,
                "The archive is no longer available; run the comparison again.",
                StatusCodes.Status409Conflict);
        }

        var location = new ArchiveEntryLocation(absolutePath, archiveSegments);
        await using var stream = await _archiveTreeService.ReadEntryContentAsync(location, limit, cancellationToken).ConfigureAwait(false);

        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        return (buffer.ToArray(), virtualPath);
    }

    private static async Task<byte[]> ReadFileAsync(string path, long limit, CancellationToken cancellationToken)
    {
        FileInfo info;
        try
        {
            info = new FileInfo(path);
            if (!info.Exists)
            {
                throw new ComparisonException(
                    ErrorCodes.FileDisappeared,
                    "The file is no longer available; run the comparison again.",
                    StatusCodes.Status409Conflict);
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new ComparisonException(ErrorCodes.AccessDenied, "Access to the file was denied.", StatusCodes.Status403Forbidden, ex.Message);
        }

        if (info.Length > limit)
        {
            throw new ComparisonException(
                ErrorCodes.FileTooLarge,
                $"The file grew beyond the {limit} byte comparison limit.",
                StatusCodes.Status413PayloadTooLarge);
        }

        try
        {
            return await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new ComparisonException(ErrorCodes.AccessDenied, "Access to the file was denied.", StatusCodes.Status403Forbidden, ex.Message);
        }
        catch (FileNotFoundException)
        {
            throw new ComparisonException(
                ErrorCodes.FileDisappeared,
                "The file is no longer available; run the comparison again.",
                StatusCodes.Status409Conflict);
        }
    }
}
