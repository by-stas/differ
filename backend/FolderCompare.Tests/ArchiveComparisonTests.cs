using System.Text;
using FolderCompare.Api.Models;
using FolderCompare.Tests.TestSupport;

namespace FolderCompare.Tests;

public sealed class ArchiveComparisonTests
{
    private static KeyValuePair<string, string> Entry(string path, string content) => new(path, content);

    [Fact]
    public async Task Identical_archives_are_unchanged()
    {
        using var left = new TempFolder("left");
        using var right = new TempFolder("right");

        var entries = new[] { Entry("config.json", "{ \"a\": 1 }"), Entry("data/test.txt", "hello") };
        left.WriteZip("package.zip", entries);
        right.WriteZip("package.zip", entries);

        var result = await new ComparisonHarness().CompareAsync(left.Path, right.Path);

        var archive = result.Root!.Require("/package.zip");
        Assert.Equal(NodeType.Archive, archive.Type);
        Assert.Equal(ComparisonStatus.Unchanged, archive.Status);
        Assert.Equal(ComparisonStatus.Unchanged, result.Root.Require("/package.zip!/config.json").Status);
    }

    [Fact]
    public async Task Archive_entries_use_the_virtual_path_format()
    {
        using var left = new TempFolder("left");
        using var right = new TempFolder("right");

        left.WriteZip("package.zip", new[] { Entry("data/test.txt", "hello") });
        right.WriteZip("package.zip", new[] { Entry("data/test.txt", "hello world") });

        var result = await new ComparisonHarness().CompareAsync(left.Path, right.Path);

        var entry = result.Root!.Require("/package.zip!/data/test.txt");
        Assert.Equal(NodeType.ArchiveFile, entry.Type);
        Assert.Equal(NodeType.ArchiveFolder, result.Root.Require("/package.zip!/data").Type);
    }

    [Fact]
    public async Task Added_removed_and_modified_entries_are_reported_inside_the_archive()
    {
        using var left = new TempFolder("left");
        using var right = new TempFolder("right");

        left.WriteZip("package.zip", new[]
        {
            Entry("config.json", "{ \"a\": 1 }"),
            Entry("readme.txt", "will be removed"),
            Entry("images/logo.txt", "same")
        });

        right.WriteZip("package.zip", new[]
        {
            Entry("config.json", "{ \"a\": 2, \"b\": 3 }"),
            Entry("settings.json", "{ \"new\": true }"),
            Entry("images/logo.txt", "same")
        });

        var result = await new ComparisonHarness().CompareAsync(left.Path, right.Path);
        var root = result.Root!;

        Assert.Equal(ComparisonStatus.Modified, root.Require("/package.zip").Status);
        Assert.Equal(ComparisonStatus.Modified, root.Require("/package.zip!/config.json").Status);
        Assert.Equal(ComparisonStatus.Removed, root.Require("/package.zip!/readme.txt").Status);
        Assert.Equal(ComparisonStatus.Added, root.Require("/package.zip!/settings.json").Status);
        Assert.Equal(ComparisonStatus.Unchanged, root.Require("/package.zip!/images").Status);
        Assert.True(result.Summary!.ArchiveEntries > 0);
    }

    [Fact]
    public async Task Same_size_entries_with_different_content_are_detected_through_the_checksum()
    {
        using var left = new TempFolder("left");
        using var right = new TempFolder("right");

        left.WriteZip("package.zip", new[] { Entry("data.txt", "AAAA") });
        right.WriteZip("package.zip", new[] { Entry("data.txt", "BBBB") });

        var result = await new ComparisonHarness(options => options.CalculateHashes = false).CompareAsync(left.Path, right.Path);

        Assert.Equal(ComparisonStatus.Modified, result.Root!.Require("/package.zip!/data.txt").Status);
    }

    [Fact]
    public async Task Modified_text_entry_can_be_opened_in_the_diff_editor()
    {
        using var left = new TempFolder("left");
        using var right = new TempFolder("right");

        left.WriteZip("package.zip", new[] { Entry("config/settings.json", "{ \"debug\": false }") });
        right.WriteZip("package.zip", new[] { Entry("config/settings.json", "{ \"debug\": true }") });

        var harness = new ComparisonHarness();
        var job = await harness.CompareJobAsync(left.Path, right.Path);

        var node = job.Result.Root!.Require("/package.zip!/config/settings.json");
        Assert.True(node.CanCompareContent);

        var content = await harness.FileContentService.GetContentAsync(job, "/package.zip!/config/settings.json");
        Assert.True(content.CanCompareContent);
        Assert.Equal("json", content.Language);
        Assert.Equal("{ \"debug\": false }", content.LeftContent);
        Assert.Equal("{ \"debug\": true }", content.RightContent);
    }

    [Fact]
    public async Task Archive_present_on_one_side_only_is_added_together_with_its_entries()
    {
        using var left = new TempFolder("left");
        using var right = new TempFolder("right");

        left.WriteFile("keep.txt", "same");
        right.WriteFile("keep.txt", "same");
        right.WriteZip("bundle.zip", new[] { Entry("a/b.txt", "inside") });

        var result = await new ComparisonHarness().CompareAsync(left.Path, right.Path);

        Assert.Equal(ComparisonStatus.Added, result.Root!.Require("/bundle.zip").Status);
        Assert.Equal(ComparisonStatus.Added, result.Root.Require("/bundle.zip!/a/b.txt").Status);
    }

    [Fact]
    public async Task Corrupted_archives_are_reported_without_failing_the_comparison()
    {
        using var left = new TempFolder("left");
        using var right = new TempFolder("right");

        left.WriteFile("broken.zip", Encoding.UTF8.GetBytes("this is definitely not a zip file"));
        right.WriteFile("broken.zip", Encoding.UTF8.GetBytes("this is definitely not a zip file at all"));

        var result = await new ComparisonHarness().CompareAsync(left.Path, right.Path);

        Assert.Equal(ComparisonState.Completed, result.Status);

        var node = result.Root!.Require("/broken.zip");
        Assert.NotNull(node.Error);
        Assert.Equal(ComparisonStatus.Modified, node.Status);
        Assert.Contains(result.Warnings, warning => warning.Contains("broken.zip", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Nested_archives_are_ignored_at_the_default_depth()
    {
        using var left = new TempFolder("left");
        using var right = new TempFolder("right");

        CreateNestedArchive(left, "outer.zip", "config.json", "{ \"v\": 1 }");
        CreateNestedArchive(right, "outer.zip", "config.json", "{ \"v\": 2 }");

        var result = await new ComparisonHarness(options => options.ArchiveMaxDepth = 1).CompareAsync(left.Path, right.Path);

        Assert.NotNull(result.Root!.Find("/outer.zip!/packages/inner.zip"));
        Assert.Null(result.Root.Find("/outer.zip!/packages/inner.zip!/config.json"));
    }

    [Fact]
    public async Task Nested_archives_are_compared_when_the_depth_allows_it()
    {
        using var left = new TempFolder("left");
        using var right = new TempFolder("right");

        CreateNestedArchive(left, "outer.zip", "config.json", "{ \"v\": 1 }");
        CreateNestedArchive(right, "outer.zip", "config.json", "{ \"version\": 2 }");

        var harness = new ComparisonHarness(options => options.ArchiveMaxDepth = 2);
        var job = await harness.CompareJobAsync(left.Path, right.Path);
        var root = job.Result.Root!;

        var nested = root.Require("/outer.zip!/packages/inner.zip!/config.json");
        Assert.Equal(ComparisonStatus.Modified, nested.Status);
        Assert.Equal(ComparisonStatus.Modified, root.Require("/outer.zip").Status);

        var content = await harness.FileContentService.GetContentAsync(job, "/outer.zip!/packages/inner.zip!/config.json");
        Assert.Equal("{ \"v\": 1 }", content.LeftContent);
        Assert.Equal("{ \"version\": 2 }", content.RightContent);
    }

    [Fact]
    public async Task Archives_exceeding_the_entry_limit_are_reported_instead_of_scanned()
    {
        using var left = new TempFolder("left");
        using var right = new TempFolder("right");

        var entries = Enumerable.Range(0, 50).Select(index => Entry($"file{index}.txt", $"content {index}")).ToArray();
        left.WriteZip("many.zip", entries);
        right.WriteZip("many.zip", entries);

        var result = await new ComparisonHarness(options => options.MaxArchiveEntries = 10).CompareAsync(left.Path, right.Path);

        var node = result.Root!.Require("/many.zip");
        Assert.NotNull(node.Error);
        Assert.Contains("entries", node.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Archives_are_skipped_entirely_when_archive_support_is_disabled()
    {
        using var left = new TempFolder("left");
        using var right = new TempFolder("right");

        left.WriteZip("package.zip", new[] { Entry("a.txt", "one") });
        right.WriteZip("package.zip", new[] { Entry("a.txt", "two") });

        var result = await new ComparisonHarness(options => options.ArchiveMaxDepth = 0).CompareAsync(left.Path, right.Path);

        var node = result.Root!.Require("/package.zip");
        Assert.Equal(NodeType.File, node.Type);
        Assert.Null(node.Children);
    }

    /// <summary>Builds <c>outer.zip</c> containing <c>packages/inner.zip</c> with a single entry.</summary>
    private static void CreateNestedArchive(TempFolder folder, string outerName, string innerEntryName, string innerEntryContent)
    {
        using var innerBuffer = new MemoryStream();

        using (var innerArchive = new System.IO.Compression.ZipArchive(innerBuffer, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = innerArchive.CreateEntry(innerEntryName);
            using var stream = entry.Open();
            stream.Write(Encoding.UTF8.GetBytes(innerEntryContent));
        }

        folder.WriteZip(outerName, new[]
        {
            new KeyValuePair<string, byte[]>("packages/inner.zip", innerBuffer.ToArray()),
            new KeyValuePair<string, byte[]>("notes.txt", Encoding.UTF8.GetBytes("outer notes"))
        });
    }
}
