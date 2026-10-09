using System.Runtime.InteropServices;
using FolderCompare.Api.Configuration;
using FolderCompare.Api.Infrastructure;
using FolderCompare.Api.Models;

namespace FolderCompare.Api.Utilities;

/// <summary>Validation and normalization of the filesystem paths supplied by clients.</summary>
public static class PathHelper
{
    /// <summary>
    /// Platform default: Windows and macOS are treated as case-insensitive, everything else
    /// as case-sensitive. Overridable through <c>Comparison:CaseSensitive</c>.
    /// </summary>
    public static bool DefaultCaseSensitive =>
        !RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
        !RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    public static bool ResolveCaseSensitivity(ComparisonOptions options)
        => options.CaseSensitive ?? DefaultCaseSensitive;

    public static StringComparer GetComparer(bool caseSensitive)
        => caseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    public static StringComparison GetComparison(bool caseSensitive)
        => caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    /// <summary>
    /// Turns a client supplied folder path into a validated absolute path.
    /// </summary>
    /// <exception cref="ComparisonException">
    /// Thrown with <see cref="ErrorCodes.PathInvalid"/>, <see cref="ErrorCodes.PathNotFound"/>,
    /// <see cref="ErrorCodes.PathNotAllowed"/> or <see cref="ErrorCodes.AccessDenied"/>.
    /// </exception>
    public static string ValidateRootFolder(string? path, string side, ComparisonOptions options)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ComparisonException(ErrorCodes.PathInvalid, $"The {side} folder path is empty.");
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path.Trim());
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ComparisonException(ErrorCodes.PathInvalid, $"The {side} folder path is not a valid path.", detail: ex.Message);
        }

        fullPath = TrimTrailingSeparator(fullPath);

        if (options.AllowedRoots.Count > 0 && !IsUnderAllowedRoot(fullPath, options))
        {
            throw new ComparisonException(
                ErrorCodes.PathNotAllowed,
                $"The {side} folder is outside the folders this server is allowed to read.",
                StatusCodes.Status403Forbidden);
        }

        try
        {
            if (!Directory.Exists(fullPath))
            {
                throw new ComparisonException(
                    ErrorCodes.PathNotFound,
                    File.Exists(fullPath)
                        ? $"The {side} path points to a file, not a folder."
                        : $"The {side} folder does not exist.",
                    StatusCodes.Status404NotFound);
            }

            // Touch the directory so permission problems surface before the scan starts.
            _ = new DirectoryInfo(fullPath).EnumerateFileSystemInfos().Any();
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new ComparisonException(
                ErrorCodes.AccessDenied,
                $"Access to the {side} folder was denied.",
                StatusCodes.Status403Forbidden,
                ex.Message);
        }
        catch (IOException ex)
        {
            throw new ComparisonException(
                ErrorCodes.PathInvalid,
                $"The {side} folder could not be read.",
                StatusCodes.Status400BadRequest,
                ex.Message);
        }

        return fullPath;
    }

    public static bool IsUnderAllowedRoot(string fullPath, ComparisonOptions options)
    {
        if (options.AllowedRoots.Count == 0)
        {
            return true;
        }

        var comparison = GetComparison(DefaultCaseSensitive);
        foreach (var allowedRoot in options.AllowedRoots)
        {
            if (string.IsNullOrWhiteSpace(allowedRoot))
            {
                continue;
            }

            string allowedFull;
            try
            {
                allowedFull = TrimTrailingSeparator(Path.GetFullPath(allowedRoot));
            }
            catch
            {
                continue;
            }

            if (fullPath.Equals(allowedFull, comparison))
            {
                return true;
            }

            if (fullPath.StartsWith(allowedFull + Path.DirectorySeparatorChar, comparison))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Resolves a normalized virtual path against a comparison root and guarantees the result
    /// stays inside that root.
    /// </summary>
    public static string ResolveInsideRoot(string rootPath, string relativePath)
    {
        var normalized = VirtualPath.Normalize(relativePath).TrimStart('/');
        var combined = Path.GetFullPath(Path.Combine(rootPath, normalized.Replace('/', Path.DirectorySeparatorChar)));
        var root = TrimTrailingSeparator(Path.GetFullPath(rootPath));
        var comparison = GetComparison(DefaultCaseSensitive);

        if (!combined.Equals(root, comparison) && !combined.StartsWith(root + Path.DirectorySeparatorChar, comparison))
        {
            throw new ComparisonException(ErrorCodes.PathInvalid, "The requested path escapes the comparison root.");
        }

        return combined;
    }

    public static string TrimTrailingSeparator(string path)
    {
        if (path.Length <= 1)
        {
            return path;
        }

        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        // Keep the separator for roots such as "/" or "C:\".
        return trimmed.Length == 0 || trimmed.EndsWith(':') ? path : trimmed;
    }
}
