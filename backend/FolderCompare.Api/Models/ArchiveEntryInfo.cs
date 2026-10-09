namespace FolderCompare.Api.Models;

/// <summary>
/// Format-agnostic description of a single entry inside an archive, as reported by an
/// <see cref="Services.IArchiveScanner"/>.
/// </summary>
/// <param name="Path">Entry path using forward slashes and no leading slash, e.g. <c>data/test.txt</c>.</param>
/// <param name="IsDirectory">True for explicit directory entries.</param>
/// <param name="Size">Uncompressed size in bytes, or null when the format does not report it.</param>
/// <param name="CompressedSize">Compressed size in bytes, or null when unknown.</param>
/// <param name="Crc32">Checksum from the archive directory, when the format provides one.</param>
public sealed record ArchiveEntryInfo(
    string Path,
    bool IsDirectory,
    long? Size,
    long? CompressedSize = null,
    uint? Crc32 = null);
