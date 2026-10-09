using System.IO.Compression;
using System.Text;

namespace FolderCompare.Tests.TestSupport;

/// <summary>Disposable scratch directory used to build the fixtures each test compares.</summary>
public sealed class TempFolder : IDisposable
{
    public TempFolder(string? name = null)
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "foldercompare-tests",
            (name ?? "run") + "-" + Guid.NewGuid().ToString("N")[..8]);

        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string WriteFile(string relativePath, string content)
        => WriteFile(relativePath, Encoding.UTF8.GetBytes(content));

    public string WriteFile(string relativePath, byte[] content)
    {
        var fullPath = System.IO.Path.Combine(Path, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath)!);
        File.WriteAllBytes(fullPath, content);
        return fullPath;
    }

    public string CreateDirectory(string relativePath)
    {
        var fullPath = System.IO.Path.Combine(Path, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
        Directory.CreateDirectory(fullPath);
        return fullPath;
    }

    /// <summary>Creates a ZIP file from an in-memory description of its entries.</summary>
    public string WriteZip(string relativePath, IEnumerable<KeyValuePair<string, string>> entries)
        => WriteZip(relativePath, entries.Select(entry => new KeyValuePair<string, byte[]>(entry.Key, Encoding.UTF8.GetBytes(entry.Value))));

    public string WriteZip(string relativePath, IEnumerable<KeyValuePair<string, byte[]>> entries)
    {
        using var buffer = new MemoryStream();

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (entryPath, content) in entries)
            {
                var entry = archive.CreateEntry(entryPath, CompressionLevel.Fastest);
                if (entryPath.EndsWith('/'))
                {
                    continue;
                }

                using var entryStream = entry.Open();
                entryStream.Write(content);
            }
        }

        return WriteFile(relativePath, buffer.ToArray());
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best effort cleanup of the scratch directory.
        }
    }
}
