using FolderCompare.Api.Configuration;
using FolderCompare.Api.Models;
using FolderCompare.Api.Utilities;
using Microsoft.Extensions.Options;

namespace FolderCompare.Api.Services;

/// <inheritdoc />
public sealed class FileComparisonService : IFileComparisonService
{
    private readonly IArchiveTreeService _archiveTreeService;
    private readonly ComparisonOptions _options;
    private readonly ILogger<FileComparisonService> _logger;

    public FileComparisonService(
        IArchiveTreeService archiveTreeService,
        IOptions<ComparisonOptions> options,
        ILogger<FileComparisonService> logger)
    {
        _archiveTreeService = archiveTreeService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<bool> AreEqualAsync(ScanNode left, ScanNode right, bool calculateHashes, CancellationToken cancellationToken = default)
    {
        if (left.Size.HasValue && right.Size.HasValue && left.Size.Value != right.Size.Value)
        {
            return false;
        }

        // A checksum from the archive directory is free, so it is used even when hashing is off.
        if (left.Crc32.HasValue && right.Crc32.HasValue)
        {
            return left.Crc32.Value == right.Crc32.Value;
        }

        if (!calculateHashes)
        {
            return left.Size.HasValue && right.Size.HasValue;
        }

        try
        {
            if (left.AbsolutePath is not null && right.AbsolutePath is not null)
            {
                var leftHash = await HashHelper.ComputeFileSha256Async(left.AbsolutePath, cancellationToken).ConfigureAwait(false);
                var rightHash = await HashHelper.ComputeFileSha256Async(right.AbsolutePath, cancellationToken).ConfigureAwait(false);
                return string.Equals(leftHash, rightHash, StringComparison.Ordinal);
            }

            if (left.ArchiveLocation is not null && right.ArchiveLocation is not null)
            {
                var limit = _options.MaxArchiveEntrySizeBytes;
                await using var leftStream = await _archiveTreeService.ReadEntryContentAsync(left.ArchiveLocation, limit, cancellationToken).ConfigureAwait(false);
                await using var rightStream = await _archiveTreeService.ReadEntryContentAsync(right.ArchiveLocation, limit, cancellationToken).ConfigureAwait(false);
                return await HashHelper.StreamsAreEqualAsync(leftStream, rightStream, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // An unreadable file is reported as modified rather than silently treated as equal.
            _logger.LogWarning(ex, "Falling back to size comparison for {Path}", left.RelativePath);
            return false;
        }

        return left.Size == right.Size;
    }

    public ContentEligibility EvaluateEligibility(ScanNode? left, ScanNode? right, ComparisonStatus status)
    {
        if (left is null || right is null)
        {
            return new ContentEligibility(false, "The file exists on one side only.");
        }

        if (status != ComparisonStatus.Modified)
        {
            return new ContentEligibility(false, "The file is identical on both sides.");
        }

        if (left.Type is NodeType.Folder or NodeType.Archive or NodeType.ArchiveFolder)
        {
            return new ContentEligibility(false, "Only files can be compared as text.");
        }

        var limit = _options.MaxDiffFileSizeBytes;
        if (left.Size > limit || right.Size > limit)
        {
            return new ContentEligibility(false, $"File exceeds the {FormatSize(limit)} comparison limit.");
        }

        if (FileTypeDetector.IsKnownBinaryExtension(left.Name))
        {
            return new ContentEligibility(false, "The file type is not supported for text comparison.");
        }

        return new ContentEligibility(true, null);
    }

    private static string FormatSize(long bytes)
    {
        var megabytes = bytes / (double)(1024 * 1024);
        return megabytes >= 1 ? $"{megabytes:0.##} MB" : $"{bytes} bytes";
    }
}
