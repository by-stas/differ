using FolderCompare.Api.Infrastructure;
using FolderCompare.Api.Models;
using FolderCompare.Tests.TestSupport;

namespace FolderCompare.Tests;

public sealed class ComparisonServiceTests
{
    [Fact]
    public async Task Completed_comparisons_stay_retrievable_by_id()
    {
        using var left = new TempFolder("left");
        using var right = new TempFolder("right");

        left.WriteFile("a.txt", "one");
        right.WriteFile("a.txt", "two");

        var harness = new ComparisonHarness();
        var result = await harness.CompareAsync(left.Path, right.Path);

        Assert.StartsWith("cmp-", result.Id);
        Assert.Equal(result.Id, harness.ComparisonService.GetResult(result.Id)!.Id);
        Assert.Null(harness.ComparisonService.GetResult("cmp-unknown"));
        Assert.NotNull(result.DurationMs);
        Assert.Equal("Completed", result.Progress.Phase);
    }

    [Fact]
    public async Task Progress_counts_the_entries_that_were_scanned_and_compared()
    {
        using var left = new TempFolder("left");
        using var right = new TempFolder("right");

        left.WriteFile("dir/a.txt", "one");
        left.WriteZip("bundle.zip", new[] { new KeyValuePair<string, string>("inner.txt", "x") });
        right.WriteFile("dir/a.txt", "one");
        right.WriteZip("bundle.zip", new[] { new KeyValuePair<string, string>("inner.txt", "x") });

        var result = await new ComparisonHarness().CompareAsync(left.Path, right.Path);

        Assert.Equal(4, result.Progress.ScannedFiles);
        Assert.Equal(2, result.Progress.ScannedFolders);
        Assert.Equal(2, result.Progress.ScannedArchives);
        Assert.True(result.Progress.ComparedNodes >= result.Summary!.Total);
    }

    [Fact]
    public async Task Cancelling_an_unknown_comparison_reports_failure()
    {
        using var left = new TempFolder("left");
        using var right = new TempFolder("right");

        var harness = new ComparisonHarness();
        var result = await harness.CompareAsync(left.Path, right.Path);

        Assert.True(harness.ComparisonService.Cancel(result.Id));
        Assert.False(harness.ComparisonService.Cancel("cmp-unknown"));
    }

    [Fact]
    public async Task Requests_can_override_the_configured_archive_depth()
    {
        using var left = new TempFolder("left");
        using var right = new TempFolder("right");

        left.WriteZip("package.zip", new[] { new KeyValuePair<string, string>("a.txt", "one") });
        right.WriteZip("package.zip", new[] { new KeyValuePair<string, string>("a.txt", "two") });

        var harness = new ComparisonHarness(options => options.ArchiveMaxDepth = 1);
        var started = await harness.ComparisonService.StartAsync(new CompareRequest
        {
            LeftPath = left.Path,
            RightPath = right.Path,
            ArchiveMaxDepth = 0
        });

        await harness.ComparisonService.GetJob(started.Id)!.Execution;
        var result = harness.ComparisonService.GetResult(started.Id)!;

        Assert.Equal(NodeType.File, result.Root!.Require("/package.zip").Type);
    }

    [Fact]
    public async Task Comparisons_are_rejected_when_a_root_is_outside_the_allowed_folders()
    {
        using var allowed = new TempFolder("allowed");
        using var outside = new TempFolder("outside");

        var inside = allowed.CreateDirectory("build");
        var harness = new ComparisonHarness(options => options.AllowedRoots.Add(allowed.Path));

        var exception = await Assert.ThrowsAsync<ComparisonException>(
            () => harness.ComparisonService.StartAsync(new CompareRequest { LeftPath = inside, RightPath = outside.Path }));

        Assert.Equal(ErrorCodes.PathNotAllowed, exception.Code);
    }

    [Fact]
    public async Task Case_insensitive_comparisons_match_names_that_differ_only_in_case()
    {
        using var left = new TempFolder("left");
        using var right = new TempFolder("right");

        left.WriteFile("Config.json", "{}");
        right.WriteFile("config.json", "{}");

        var sensitive = await new ComparisonHarness(options => options.CaseSensitive = true).CompareAsync(left.Path, right.Path);
        Assert.Equal(1, sensitive.Summary!.Added);
        Assert.Equal(1, sensitive.Summary.Removed);

        var insensitive = await new ComparisonHarness(options => options.CaseSensitive = false).CompareAsync(left.Path, right.Path);
        Assert.Equal(0, insensitive.Summary!.Added);
        Assert.Equal(0, insensitive.Summary.Removed);
        Assert.Equal(1, insensitive.Summary.Unchanged);
    }

    [Fact]
    public async Task Comparisons_are_evicted_once_the_store_is_full()
    {
        using var left = new TempFolder("left");
        using var right = new TempFolder("right");

        var harness = new ComparisonHarness(options => options.MaxStoredComparisons = 2);

        var first = await harness.CompareAsync(left.Path, right.Path);
        await harness.CompareAsync(left.Path, right.Path);
        await harness.CompareAsync(left.Path, right.Path);

        Assert.Null(harness.ComparisonService.GetResult(first.Id));
    }
}
