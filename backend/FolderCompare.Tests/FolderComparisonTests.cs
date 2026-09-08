using FolderCompare.Api.Models;
using FolderCompare.Tests.TestSupport;

namespace FolderCompare.Tests;

public sealed class FolderComparisonTests
{
    [Fact]
    public async Task Identical_folders_report_everything_as_unchanged()
    {
        using var left = new TempFolder("left");
        using var right = new TempFolder("right");

        left.WriteFile("config.json", "{ \"a\": 1 }");
        left.WriteFile("logs/app.log", "hello");
        right.WriteFile("config.json", "{ \"a\": 1 }");
        right.WriteFile("logs/app.log", "hello");

        var result = await new ComparisonHarness().CompareAsync(left.Path, right.Path);

        Assert.Equal(ComparisonState.Completed, result.Status);
        Assert.Equal(ComparisonStatus.Unchanged, result.Root!.Status);
        Assert.Equal(0, result.Summary!.Added);
        Assert.Equal(0, result.Summary.Removed);
        Assert.Equal(0, result.Summary.Modified);
        Assert.Equal(3, result.Summary.Unchanged);
    }

    [Fact]
    public async Task File_only_in_right_folder_is_added()
    {
        using var left = new TempFolder("left");
        using var right = new TempFolder("right");

        left.WriteFile("keep.txt", "same");
        right.WriteFile("keep.txt", "same");
        right.WriteFile("new-file.txt", "brand new");

        var result = await new ComparisonHarness().CompareAsync(left.Path, right.Path);

        var added = result.Root!.Require("/new-file.txt");
        Assert.Equal(ComparisonStatus.Added, added.Status);
        Assert.Null(added.LeftSize);
        Assert.Equal(9, added.RightSize);
        Assert.Equal(1, result.Summary!.Added);
        Assert.Equal(ComparisonStatus.Modified, result.Root!.Status);
    }

    [Fact]
    public async Task File_only_in_left_folder_is_removed()
    {
        using var left = new TempFolder("left");
        using var right = new TempFolder("right");

        left.WriteFile("readme.txt", "gone soon");
        right.WriteFile("keep.txt", "kept");

        var result = await new ComparisonHarness().CompareAsync(left.Path, right.Path);

        var removed = result.Root!.Require("/readme.txt");
        Assert.Equal(ComparisonStatus.Removed, removed.Status);
        Assert.Equal(9, removed.LeftSize);
        Assert.Null(removed.RightSize);
        Assert.False(removed.CanCompareContent);
        Assert.Equal(1, result.Summary!.Removed);
        Assert.Equal(1, result.Summary.Added);
    }

    [Fact]
    public async Task Different_size_marks_the_file_as_modified()
    {
        using var left = new TempFolder("left");
        using var right = new TempFolder("right");

        left.WriteFile("config.json", "{ \"a\": 1 }");
        right.WriteFile("config.json", "{ \"a\": 1, \"b\": 2 }");

        var result = await new ComparisonHarness().CompareAsync(left.Path, right.Path);

        var node = result.Root!.Require("/config.json");
        Assert.Equal(ComparisonStatus.Modified, node.Status);
        Assert.True(node.CanCompareContent);
        Assert.Equal(1, result.Summary!.Modified);
    }

    [Fact]
    public async Task Same_size_but_different_content_is_detected_by_hashing()
    {
        using var left = new TempFolder("left");
        using var right = new TempFolder("right");

        left.WriteFile("data.txt", "AAAA");
        right.WriteFile("data.txt", "BBBB");

        var result = await new ComparisonHarness(options => options.CalculateHashes = true).CompareAsync(left.Path, right.Path);

        Assert.Equal(ComparisonStatus.Modified, result.Root!.Require("/data.txt").Status);
    }

    [Fact]
    public async Task Same_size_files_are_reported_unchanged_when_hashing_is_disabled()
    {
        using var left = new TempFolder("left");
        using var right = new TempFolder("right");

        left.WriteFile("data.txt", "AAAA");
        right.WriteFile("data.txt", "BBBB");

        var result = await new ComparisonHarness(options => options.CalculateHashes = false).CompareAsync(left.Path, right.Path);

        Assert.Equal(ComparisonStatus.Unchanged, result.Root!.Require("/data.txt").Status);
    }

    [Fact]
    public async Task Added_directory_is_reported_with_all_of_its_children()
    {
        using var left = new TempFolder("left");
        using var right = new TempFolder("right");

        left.WriteFile("keep.txt", "same");
        right.WriteFile("keep.txt", "same");
        right.WriteFile("src/new/deep.cs", "class C {}");

        var result = await new ComparisonHarness().CompareAsync(left.Path, right.Path);

        Assert.Equal(ComparisonStatus.Added, result.Root!.Require("/src").Status);
        Assert.Equal(ComparisonStatus.Added, result.Root.Require("/src/new").Status);
        Assert.Equal(ComparisonStatus.Added, result.Root.Require("/src/new/deep.cs").Status);
        Assert.Equal(NodeType.Folder, result.Root.Require("/src").Type);
    }

    [Fact]
    public async Task Removed_directory_is_reported_with_all_of_its_children()
    {
        using var left = new TempFolder("left");
        using var right = new TempFolder("right");

        left.WriteFile("old/legacy/file.txt", "legacy");
        right.WriteFile("keep.txt", "kept");

        var result = await new ComparisonHarness().CompareAsync(left.Path, right.Path);

        Assert.Equal(ComparisonStatus.Removed, result.Root!.Require("/old").Status);
        Assert.Equal(ComparisonStatus.Removed, result.Root.Require("/old/legacy/file.txt").Status);
    }

    [Fact]
    public async Task Modification_propagates_up_through_nested_folders()
    {
        using var left = new TempFolder("left");
        using var right = new TempFolder("right");

        left.WriteFile("src/a/b/file1.cs", "one");
        left.WriteFile("src/a/b/file2.cs", "two");
        right.WriteFile("src/a/b/file1.cs", "one");
        right.WriteFile("src/a/b/file2.cs", "two-changed");

        var result = await new ComparisonHarness().CompareAsync(left.Path, right.Path);

        Assert.Equal(ComparisonStatus.Unchanged, result.Root!.Require("/src/a/b/file1.cs").Status);
        Assert.Equal(ComparisonStatus.Modified, result.Root.Require("/src/a/b/file2.cs").Status);
        Assert.Equal(ComparisonStatus.Modified, result.Root.Require("/src/a/b").Status);
        Assert.Equal(ComparisonStatus.Modified, result.Root.Require("/src/a").Status);
        Assert.Equal(ComparisonStatus.Modified, result.Root.Require("/src").Status);
        Assert.Equal(ComparisonStatus.Modified, result.Root!.Status);
    }

    [Fact]
    public async Task A_file_replaced_by_a_folder_is_modified()
    {
        using var left = new TempFolder("left");
        using var right = new TempFolder("right");

        left.WriteFile("thing", "I am a file");
        right.WriteFile("thing/inner.txt", "I am a folder now");

        var result = await new ComparisonHarness().CompareAsync(left.Path, right.Path);

        var node = result.Root!.Require("/thing");
        Assert.Equal(ComparisonStatus.Modified, node.Status);
        Assert.False(node.CanCompareContent);
    }

    [Fact]
    public async Task Summary_counts_every_node_except_the_root()
    {
        using var left = new TempFolder("left");
        using var right = new TempFolder("right");

        left.WriteFile("a.txt", "a");
        left.WriteFile("dir/b.txt", "b");
        right.WriteFile("a.txt", "a");
        right.WriteFile("dir/b.txt", "bb");
        right.WriteFile("dir/c.txt", "c");

        var result = await new ComparisonHarness().CompareAsync(left.Path, right.Path);
        var summary = result.Summary!;

        Assert.Equal(4, summary.Total);
        Assert.Equal(1, summary.Added);
        Assert.Equal(0, summary.Removed);
        Assert.Equal(2, summary.Modified);
        Assert.Equal(1, summary.Unchanged);
        Assert.Equal(1, summary.Folders);
        Assert.Equal(3, summary.Files);
        Assert.Equal(summary.Total, summary.Added + summary.Removed + summary.Modified + summary.Unchanged);
    }

    [Fact]
    public async Task Empty_folders_compare_cleanly()
    {
        using var left = new TempFolder("left");
        using var right = new TempFolder("right");

        var result = await new ComparisonHarness().CompareAsync(left.Path, right.Path);

        Assert.Equal(ComparisonState.Completed, result.Status);
        Assert.Equal(ComparisonStatus.Unchanged, result.Root!.Status);
        Assert.Equal(0, result.Summary!.Total);
    }

    [Fact]
    public async Task Relative_paths_are_normalized_independently_of_the_roots()
    {
        using var left = new TempFolder("build-a");
        using var right = new TempFolder("build-b");

        left.WriteFile("src/config.json", "{}");
        right.WriteFile("src/config.json", "{ }");

        var result = await new ComparisonHarness().CompareAsync(left.Path, right.Path);

        var paths = result.Root!.Flatten().Select(node => node.RelativePath).ToList();
        Assert.Contains("/src/config.json", paths);
        Assert.DoesNotContain(paths, path => path.Contains(left.Path, StringComparison.Ordinal));
        Assert.All(paths, path => Assert.StartsWith("/", path));
    }
}
