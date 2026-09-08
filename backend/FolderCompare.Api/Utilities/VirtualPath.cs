using System.Text;

namespace FolderCompare.Api.Utilities;

/// <summary>
/// Helpers for the virtual path format used throughout the comparison tree:
/// <c>/folder/package.zip!/data/test.txt</c>. Everything left of the first <c>!/</c> lives on
/// disk, every following segment addresses an entry inside the preceding archive.
/// </summary>
public static class VirtualPath
{
    public const string ArchiveSeparator = "!/";

    /// <summary>Appends a child name to a normalized parent path.</summary>
    public static string Combine(string parent, string name)
    {
        if (string.IsNullOrEmpty(parent) || parent == "/")
        {
            return "/" + name;
        }

        return parent.EndsWith('/') ? parent + name : parent + "/" + name;
    }

    /// <summary>Appends an archive entry path to the path of the archive itself.</summary>
    public static string CombineArchive(string archivePath, string entryPath)
        => archivePath + ArchiveSeparator + entryPath.TrimStart('/');

    /// <summary>
    /// Normalizes a client supplied path: converts backslashes, collapses duplicate and
    /// trailing slashes, resolves <c>.</c> segments and rejects <c>..</c> traversal.
    /// </summary>
    public static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "/";
        }

        var builder = new StringBuilder();
        var parts = path.Replace('\\', '/').Split(ArchiveSeparator, StringSplitOptions.None);

        for (var i = 0; i < parts.Length; i++)
        {
            var normalizedPart = NormalizeSegmentList(parts[i]);
            if (i == 0)
            {
                builder.Append(normalizedPart.Length == 0 ? "/" : "/" + normalizedPart);
            }
            else
            {
                builder.Append(ArchiveSeparator).Append(normalizedPart);
            }
        }

        return builder.ToString();
    }

    /// <summary>Splits a virtual path into its on-disk part and the archive entry segments.</summary>
    public static (string DiskPath, IReadOnlyList<string> ArchiveSegments) Split(string virtualPath)
    {
        var normalized = Normalize(virtualPath);
        var parts = normalized.Split(ArchiveSeparator, StringSplitOptions.None);
        return (parts[0], parts.Skip(1).ToList());
    }

    public static bool ContainsArchiveSegment(string virtualPath) => virtualPath.Contains(ArchiveSeparator, StringComparison.Ordinal);

    /// <summary>
    /// Validates an entry path coming from an archive directory. Absolute, rooted and
    /// traversing entries are rejected because they are a classic archive attack.
    /// </summary>
    public static bool IsSafeArchiveEntryPath(string entryPath)
    {
        if (string.IsNullOrWhiteSpace(entryPath))
        {
            return false;
        }

        var candidate = entryPath.Replace('\\', '/');
        if (candidate.StartsWith('/') || candidate.Contains(':'))
        {
            return false;
        }

        foreach (var segment in candidate.Split('/'))
        {
            if (segment == "..")
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Splits an archive entry path into its non-empty segments.</summary>
    public static string[] SplitEntryPath(string entryPath)
        => entryPath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);

    private static string NormalizeSegmentList(string value)
    {
        var segments = new List<string>();
        foreach (var segment in value.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                throw new ArgumentException("Path traversal is not allowed.", nameof(value));
            }

            segments.Add(segment);
        }

        return string.Join('/', segments);
    }
}
