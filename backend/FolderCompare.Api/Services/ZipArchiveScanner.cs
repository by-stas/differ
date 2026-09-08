using System.IO.Compression;
using FolderCompare.Api.Configuration;
using FolderCompare.Api.Infrastructure;
using FolderCompare.Api.Models;
using FolderCompare.Api.Utilities;
using Microsoft.Extensions.Options;

namespace FolderCompare.Api.Services;

/// <summary>
/// ZIP support built on <see cref="System.IO.Compression"/>. Archives are opened read-only and
/// never extracted; only the central directory is walked unless an entry is explicitly read.
/// </summary>
public sealed class ZipArchiveScanner : IArchiveScanner
{
    private readonly ComparisonOptions _options;
    private readonly ILogger<ZipArchiveScanner> _logger;

    public ZipArchiveScanner(IOptions<ComparisonOptions> options, ILogger<ZipArchiveScanner> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public string FormatName => "ZIP";

    public bool CanHandle(string filePath) => FileTypeDetector.IsZipExtension(filePath);

    public async Task<IReadOnlyCollection<ArchiveEntryInfo>> ScanAsync(string filePath, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous);

        return await ScanAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    public Task<IReadOnlyCollection<ArchiveEntryInfo>> ScanAsync(Stream archiveStream, CancellationToken cancellationToken = default)
    {
        using var archive = OpenArchive(archiveStream);

        var entries = new List<ArchiveEntryInfo>();
        long totalUncompressed = 0;
        long totalCompressed = 0;

        try
        {
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (entries.Count >= _options.MaxArchiveEntries)
                {
                    throw new ArchiveLimitException(
                        $"The archive contains more than {_options.MaxArchiveEntries} entries and was not inspected further.");
                }

                var rawName = entry.FullName.Replace('\\', '/');
                var isDirectory = rawName.EndsWith('/') || string.IsNullOrEmpty(entry.Name);
                var normalizedName = rawName.Trim('/');

                if (normalizedName.Length == 0)
                {
                    continue;
                }

                if (!VirtualPath.IsSafeArchiveEntryPath(normalizedName))
                {
                    _logger.LogWarning("Skipping unsafe archive entry {Entry}", normalizedName);
                    continue;
                }

                totalUncompressed += entry.Length;
                totalCompressed += entry.CompressedLength;

                if (totalUncompressed > _options.MaxTotalUncompressedBytes)
                {
                    throw new ArchiveLimitException(
                        "The archive expands to more data than the configured maximum uncompressed size.");
                }

                if (totalCompressed > 0 && totalUncompressed / (double)totalCompressed > _options.MaxCompressionRatio)
                {
                    throw new ArchiveLimitException(
                        "The archive compression ratio exceeds the configured maximum and was rejected.");
                }

                entries.Add(new ArchiveEntryInfo(
                    normalizedName,
                    isDirectory,
                    isDirectory ? null : entry.Length,
                    isDirectory ? null : entry.CompressedLength,
                    isDirectory ? null : entry.Crc32));
            }
        }
        catch (InvalidDataException ex)
        {
            throw new ComparisonException(
                ErrorCodes.ArchiveCorrupted,
                "The archive is corrupted or not a valid ZIP file.",
                StatusCodes.Status422UnprocessableEntity,
                ex.Message,
                ex);
        }

        return Task.FromResult<IReadOnlyCollection<ArchiveEntryInfo>>(entries);
    }

    public async Task<Stream> ReadEntryAsync(Stream archiveStream, string entryPath, long maxBytes, CancellationToken cancellationToken = default)
    {
        using var archive = OpenArchive(archiveStream);

        var entry = archive.GetEntry(entryPath)
                    ?? archive.Entries.FirstOrDefault(e =>
                        string.Equals(e.FullName.Replace('\\', '/').Trim('/'), entryPath, StringComparison.Ordinal));

        if (entry is null)
        {
            throw new ComparisonException(
                ErrorCodes.NodeNotFound,
                $"The archive does not contain an entry named '{entryPath}'.",
                StatusCodes.Status404NotFound);
        }

        if (entry.Length > maxBytes)
        {
            throw new ArchiveLimitException($"The archive entry '{entryPath}' is larger than the {maxBytes} byte read limit.");
        }

        var buffer = new MemoryStream(entry.Length > 0 && entry.Length < int.MaxValue ? (int)entry.Length : 0);

        try
        {
            await using var entryStream = entry.Open();
            await CopyWithLimitAsync(entryStream, buffer, maxBytes, entryPath, cancellationToken).ConfigureAwait(false);
            buffer.Position = 0;
            return buffer;
        }
        catch (InvalidDataException ex)
        {
            await buffer.DisposeAsync().ConfigureAwait(false);
            throw new ComparisonException(
                ErrorCodes.ArchiveCorrupted,
                $"The archive entry '{entryPath}' could not be read; the archive appears to be corrupted.",
                StatusCodes.Status422UnprocessableEntity,
                ex.Message,
                ex);
        }
        catch
        {
            await buffer.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static ZipArchive OpenArchive(Stream archiveStream)
    {
        if (archiveStream.CanSeek)
        {
            archiveStream.Position = 0;
        }

        try
        {
            return new ZipArchive(archiveStream, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException ex)
        {
            throw new ComparisonException(
                ErrorCodes.ArchiveCorrupted,
                "The archive is corrupted or not a valid ZIP file.",
                StatusCodes.Status422UnprocessableEntity,
                ex.Message,
                ex);
        }
    }

    /// <summary>
    /// Copies at most <paramref name="maxBytes"/>; a directory entry that under-reports its
    /// size cannot make us write an unbounded amount into memory.
    /// </summary>
    private static async Task CopyWithLimitAsync(Stream source, Stream destination, long maxBytes, string entryPath, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long copied = 0;

        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return;
            }

            copied += read;
            if (copied > maxBytes)
            {
                throw new ArchiveLimitException($"The archive entry '{entryPath}' expands beyond the {maxBytes} byte read limit.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
    }
}
