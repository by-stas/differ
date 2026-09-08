using System.Text;
using FolderCompare.Api.Configuration;
using FolderCompare.Api.Infrastructure;
using FolderCompare.Api.Models;
using FolderCompare.Api.Utilities;
using FolderCompare.Tests.TestSupport;

namespace FolderCompare.Tests;

public sealed class VirtualPathTests
{
    [Theory]
    [InlineData("src\\config.json", "/src/config.json")]
    [InlineData("/src//config.json", "/src/config.json")]
    [InlineData("src/./config.json", "/src/config.json")]
    [InlineData("", "/")]
    [InlineData("/", "/")]
    public void Normalize_produces_root_relative_forward_slash_paths(string input, string expected)
        => Assert.Equal(expected, VirtualPath.Normalize(input));

    [Fact]
    public void Normalize_keeps_the_archive_separator()
        => Assert.Equal("/package.zip!/config/settings.json", VirtualPath.Normalize("/package.zip!/config/settings.json"));

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("/src/../../escape.txt")]
    [InlineData("/package.zip!/../escape.txt")]
    public void Normalize_rejects_traversal(string input)
        => Assert.Throws<ArgumentException>(() => VirtualPath.Normalize(input));

    [Fact]
    public void Split_separates_the_disk_path_from_the_archive_segments()
    {
        var (diskPath, segments) = VirtualPath.Split("/dir/outer.zip!/packages/inner.zip!/config.json");

        Assert.Equal("/dir/outer.zip", diskPath);
        Assert.Equal(new[] { "packages/inner.zip", "config.json" }, segments);
    }

    [Theory]
    [InlineData("../../windows/system32/example", false)]
    [InlineData("/absolute/entry", false)]
    [InlineData("C:/windows/entry", false)]
    [InlineData("data/test.txt", true)]
    public void Unsafe_archive_entries_are_rejected(string entryPath, bool expected)
        => Assert.Equal(expected, VirtualPath.IsSafeArchiveEntryPath(entryPath));
}

public sealed class PathHelperTests
{
    [Fact]
    public void Missing_folders_are_reported_as_path_not_found()
    {
        var options = new ComparisonOptions();
        var exception = Assert.Throws<ComparisonException>(
            () => PathHelper.ValidateRootFolder(Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid()), "left", options));

        Assert.Equal(ErrorCodes.PathNotFound, exception.Code);
    }

    [Fact]
    public void Empty_paths_are_reported_as_invalid()
    {
        var exception = Assert.Throws<ComparisonException>(() => PathHelper.ValidateRootFolder("   ", "right", new ComparisonOptions()));
        Assert.Equal(ErrorCodes.PathInvalid, exception.Code);
    }

    [Fact]
    public void A_file_path_is_not_accepted_as_a_folder()
    {
        using var folder = new TempFolder();
        var filePath = folder.WriteFile("a.txt", "content");

        var exception = Assert.Throws<ComparisonException>(() => PathHelper.ValidateRootFolder(filePath, "left", new ComparisonOptions()));
        Assert.Equal(ErrorCodes.PathNotFound, exception.Code);
    }

    [Fact]
    public void Folders_outside_the_allowed_roots_are_rejected()
    {
        using var allowed = new TempFolder("allowed");
        using var outside = new TempFolder("outside");

        var options = new ComparisonOptions { AllowedRoots = { allowed.Path } };

        var exception = Assert.Throws<ComparisonException>(() => PathHelper.ValidateRootFolder(outside.Path, "left", options));
        Assert.Equal(ErrorCodes.PathNotAllowed, exception.Code);

        var inside = allowed.CreateDirectory("nested");
        Assert.Equal(inside, PathHelper.ValidateRootFolder(inside, "left", options));
    }

    [Fact]
    public void Resolving_a_relative_path_cannot_escape_the_root()
    {
        using var folder = new TempFolder();
        Assert.Throws<ArgumentException>(() => PathHelper.ResolveInsideRoot(folder.Path, "/../outside.txt"));
    }
}

public sealed class FileTypeDetectorTests
{
    [Theory]
    [InlineData("config.json", "json")]
    [InlineData("Program.cs", "csharp")]
    [InlineData("app.component.ts", "typescript")]
    [InlineData("styles.scss", "scss")]
    [InlineData("query.sql", "sql")]
    [InlineData("notes.unknown", "plaintext")]
    public void Languages_are_mapped_from_the_extension(string fileName, string expected)
        => Assert.Equal(expected, FileTypeDetector.GetLanguage(fileName));

    [Fact]
    public void Null_bytes_mark_content_as_binary()
        => Assert.False(FileTypeDetector.LooksLikeText(new byte[] { 0x48, 0x00, 0x49 }));

    [Fact]
    public void Plain_ascii_is_text()
        => Assert.True(FileTypeDetector.LooksLikeText("hello\nworld\r\n"u8));

    [Fact]
    public void Empty_content_is_text()
        => Assert.True(FileTypeDetector.LooksLikeText(ReadOnlySpan<byte>.Empty));

    [Fact]
    public void Invalid_utf8_falls_back_to_a_single_byte_encoding()
    {
        var result = FileTypeDetector.Decode(new byte[] { 0x48, 0xE9, 0x6C, 0x6C, 0x6F });

        Assert.True(result.Success);
        Assert.Equal("Héllo", result.Text);
        Assert.Equal("iso-8859-1", result.EncodingName);
        Assert.NotNull(result.Reason);
    }

    [Fact]
    public void Binary_content_is_not_decoded()
    {
        var result = FileTypeDetector.Decode(new byte[] { 0x00, 0x01, 0x02 });

        Assert.False(result.Success);
        Assert.Null(result.Text);
    }

    [Fact]
    public void Utf8_text_round_trips()
    {
        var result = FileTypeDetector.Decode(Encoding.UTF8.GetBytes("naïve café"));

        Assert.True(result.Success);
        Assert.Equal("naïve café", result.Text);
        Assert.Equal("utf-8", result.EncodingName);
    }

    [Theory]
    [InlineData("package.zip", true)]
    [InlineData("library.jar", true)]
    [InlineData("notes.txt", false)]
    public void Zip_like_extensions_are_recognised(string fileName, bool expected)
        => Assert.Equal(expected, FileTypeDetector.IsZipExtension(fileName));
}
