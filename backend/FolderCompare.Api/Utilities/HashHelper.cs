using System.Security.Cryptography;

namespace FolderCompare.Api.Utilities;

/// <summary>Streaming SHA-256 helpers; file contents are never buffered in full.</summary>
public static class HashHelper
{
    private const int BufferSize = 128 * 1024;

    public static async Task<string> ComputeSha256Async(Stream stream, CancellationToken cancellationToken = default)
    {
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }

    public static async Task<string> ComputeFileSha256Async(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        return await ComputeSha256Async(stream, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Compares two streams chunk by chunk without materializing them. Used for archive
    /// entries where a cheap checksum is unavailable.
    /// </summary>
    public static async Task<bool> StreamsAreEqualAsync(Stream left, Stream right, CancellationToken cancellationToken = default)
    {
        var leftBuffer = new byte[BufferSize];
        var rightBuffer = new byte[BufferSize];

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var leftRead = await ReadBlockAsync(left, leftBuffer, cancellationToken).ConfigureAwait(false);
            var rightRead = await ReadBlockAsync(right, rightBuffer, cancellationToken).ConfigureAwait(false);

            if (leftRead != rightRead)
            {
                return false;
            }

            if (leftRead == 0)
            {
                return true;
            }

            if (!leftBuffer.AsSpan(0, leftRead).SequenceEqual(rightBuffer.AsSpan(0, rightRead)))
            {
                return false;
            }
        }
    }

    private static async Task<int> ReadBlockAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }
}
