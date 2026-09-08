using System.Text;

namespace FolderCompare.Api.Utilities;

public sealed record TextDecodeResult(bool Success, string? Text, string EncodingName, string? Reason);

/// <summary>
/// Decides whether a file can be shown in the diff editor and which Monaco language to use.
/// Extension is only the first signal: content is sniffed for binary markers before the
/// backend hands anything to the frontend.
/// </summary>
public static class FileTypeDetector
{
    /// <summary>Number of leading bytes inspected when sniffing for binary content.</summary>
    public const int SniffLength = 8 * 1024;

    private static readonly Dictionary<string, string> LanguageByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".txt"] = "plaintext",
        [".log"] = "plaintext",
        [".md"] = "markdown",
        [".markdown"] = "markdown",
        [".json"] = "json",
        [".jsonc"] = "json",
        [".xml"] = "xml",
        [".xsd"] = "xml",
        [".xsl"] = "xml",
        [".csproj"] = "xml",
        [".props"] = "xml",
        [".targets"] = "xml",
        [".svg"] = "xml",
        [".yaml"] = "yaml",
        [".yml"] = "yaml",
        [".toml"] = "ini",
        [".ini"] = "ini",
        [".config"] = "xml",
        [".cs"] = "csharp",
        [".ts"] = "typescript",
        [".tsx"] = "typescript",
        [".js"] = "javascript",
        [".jsx"] = "javascript",
        [".mjs"] = "javascript",
        [".cjs"] = "javascript",
        [".html"] = "html",
        [".htm"] = "html",
        [".css"] = "css",
        [".scss"] = "scss",
        [".less"] = "less",
        [".sql"] = "sql",
        [".csv"] = "plaintext",
        [".tsv"] = "plaintext",
        [".sh"] = "shell",
        [".bash"] = "shell",
        [".ps1"] = "powershell",
        [".bat"] = "bat",
        [".cmd"] = "bat",
        [".py"] = "python",
        [".rb"] = "ruby",
        [".go"] = "go",
        [".rs"] = "rust",
        [".java"] = "java",
        [".kt"] = "kotlin",
        [".php"] = "php",
        [".c"] = "c",
        [".h"] = "c",
        [".cpp"] = "cpp",
        [".hpp"] = "cpp",
        [".swift"] = "swift",
        [".vb"] = "vb",
        [".razor"] = "razor",
        [".cshtml"] = "razor",
        [".graphql"] = "graphql",
        [".dockerfile"] = "dockerfile",
        [".env"] = "plaintext",
        [".gitignore"] = "plaintext",
        [".editorconfig"] = "ini",
        [".patch"] = "diff",
        [".diff"] = "diff"
    };

    private static readonly HashSet<string> FileNameTextOverrides = new(StringComparer.OrdinalIgnoreCase)
    {
        "dockerfile", "makefile", "license", "readme", "changelog", ".gitignore", ".gitattributes", ".editorconfig", ".env"
    };

    private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".so", ".dylib", ".bin", ".obj", ".o", ".a", ".lib", ".pdb",
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".webp", ".tif", ".tiff", ".psd",
        ".mp3", ".wav", ".ogg", ".flac", ".mp4", ".avi", ".mkv", ".mov", ".wmv",
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".odt", ".ods",
        ".zip", ".7z", ".rar", ".tar", ".gz", ".bz2", ".xz", ".jar", ".war", ".nupkg", ".whl",
        ".ttf", ".otf", ".woff", ".woff2", ".eot", ".class", ".pyc", ".wasm", ".db", ".sqlite"
    };

    private static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".jar", ".war", ".nupkg", ".vsix", ".apk"
    };

    public static string GetLanguage(string fileName)
    {
        var name = Path.GetFileName(fileName);
        if (FileNameTextOverrides.Contains(name))
        {
            return name.Equals("dockerfile", StringComparison.OrdinalIgnoreCase) ? "dockerfile" : "plaintext";
        }

        var extension = Path.GetExtension(name);
        return LanguageByExtension.TryGetValue(extension, out var language) ? language : "plaintext";
    }

    public static bool IsKnownTextExtension(string fileName)
    {
        var name = Path.GetFileName(fileName);
        return FileNameTextOverrides.Contains(name) || LanguageByExtension.ContainsKey(Path.GetExtension(name));
    }

    public static bool IsKnownBinaryExtension(string fileName)
        => BinaryExtensions.Contains(Path.GetExtension(Path.GetFileName(fileName)));

    /// <summary>Extensions the ZIP scanner recognises. Kept here so detection lives in one place.</summary>
    public static bool IsZipExtension(string fileName)
        => ArchiveExtensions.Contains(Path.GetExtension(Path.GetFileName(fileName)));

    /// <summary>
    /// Heuristic binary check over a leading sample: a byte-order mark means text, a NUL byte
    /// means binary, and a high share of non-printable control characters means binary.
    /// </summary>
    public static bool LooksLikeText(ReadOnlySpan<byte> sample)
    {
        if (sample.Length == 0)
        {
            return true;
        }

        if (HasBom(sample, out _))
        {
            return true;
        }

        var control = 0;
        foreach (var b in sample)
        {
            if (b == 0)
            {
                return false;
            }

            if (b < 0x09 || (b > 0x0D && b < 0x20) || b == 0x7F)
            {
                control++;
            }
        }

        return control * 100 / sample.Length < 5;
    }

    public static bool HasBom(ReadOnlySpan<byte> sample, out Encoding? encoding)
    {
        encoding = null;

        if (sample.Length >= 4 && sample[0] == 0xFF && sample[1] == 0xFE && sample[2] == 0x00 && sample[3] == 0x00)
        {
            encoding = new UTF32Encoding(bigEndian: false, byteOrderMark: true);
            return true;
        }

        if (sample.Length >= 4 && sample[0] == 0x00 && sample[1] == 0x00 && sample[2] == 0xFE && sample[3] == 0xFF)
        {
            encoding = new UTF32Encoding(bigEndian: true, byteOrderMark: true);
            return true;
        }

        if (sample.Length >= 3 && sample[0] == 0xEF && sample[1] == 0xBB && sample[2] == 0xBF)
        {
            encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
            return true;
        }

        if (sample.Length >= 2 && sample[0] == 0xFF && sample[1] == 0xFE)
        {
            encoding = new UnicodeEncoding(bigEndian: false, byteOrderMark: true);
            return true;
        }

        if (sample.Length >= 2 && sample[0] == 0xFE && sample[1] == 0xFF)
        {
            encoding = new UnicodeEncoding(bigEndian: true, byteOrderMark: true);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Decodes bytes to text. A byte-order mark wins, otherwise strict UTF-8 is attempted and
    /// Latin-1 is used as a lossless last resort so that mostly-ASCII files still diff.
    /// </summary>
    public static TextDecodeResult Decode(byte[] bytes)
    {
        if (!LooksLikeText(bytes.AsSpan(0, Math.Min(bytes.Length, SniffLength))))
        {
            return new TextDecodeResult(false, null, "binary", "The file appears to contain binary data.");
        }

        if (HasBom(bytes, out var bomEncoding) && bomEncoding is not null)
        {
            var preambleLength = bomEncoding.GetPreamble().Length;
            var text = bomEncoding.GetString(bytes, preambleLength, bytes.Length - preambleLength);
            return new TextDecodeResult(true, text, bomEncoding.WebName, null);
        }

        try
        {
            var strictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            return new TextDecodeResult(true, strictUtf8.GetString(bytes), "utf-8", null);
        }
        catch (DecoderFallbackException)
        {
            // Not valid UTF-8; fall back to a single byte encoding that never throws.
        }

        return new TextDecodeResult(true, Encoding.Latin1.GetString(bytes), "iso-8859-1", "Encoding could not be detected; the file was decoded as ISO-8859-1.");
    }
}
