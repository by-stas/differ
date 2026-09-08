using System.Text;
using FolderCompare.Api.Models;
using FolderCompare.Tests.TestSupport;

namespace FolderCompare.Tests;

public sealed class FileEligibilityTests
{
    [Fact]
    public async Task Modified_text_file_below_the_limit_can_be_compared()
    {
        using var left = new TempFolder("left");
        using var right = new TempFolder("right");

        left.WriteFile("notes.md", "# Title");
        right.WriteFile("notes.md", "# Different title");

        var harness = new ComparisonHarness();
        var job = await harness.CompareJobAsync(left.Path, right.Path);

        var node = job.Result.Root!.Require("/notes.md");
        Assert.True(node.CanCompareContent);

        var content = await harness.FileContentService.GetContentAsync(job, "/notes.md");
        Assert.True(content.CanCompareContent);
        Assert.Equal("markdown", content.Language);
        Assert.Equal("# Title", content.LeftContent);
        Assert.Equal("# Different title", content.RightContent);
    }

    [Fact]
    public async Task Files_above_the_limit_are_refused_by_the_backend()
    {
        using var left = new TempFolder("left");
        using var right = new TempFolder("right");

        var limit = 4 * 1024;
        left.WriteFile("big.txt", new string('a', limit + 10));
        right.WriteFile("big.txt", new string('b', limit + 20));

        var harness = new ComparisonHarness(options => options.MaxDiffFileSizeBytes = limit);
        var job = await harness.CompareJobAsync(left.Path, right.Path);

        var node = job.Result.Root!.Require("/big.txt");
        Assert.Equal(ComparisonStatus.Modified, node.Status);
        Assert.False(node.CanCompareContent);
        Assert.Contains("limit", node.ContentUnavailableReason!, StringComparison.OrdinalIgnoreCase);

        var content = await harness.FileContentService.GetContentAsync(job, "/big.txt");
        Assert.False(content.CanCompareContent);
        Assert.Null(content.LeftContent);
    }

    [Fact]
    public async Task Known_binary_extensions_are_not_offered_for_text_comparison()
    {
        using var left = new TempFolder("left");
        using var right = new TempFolder("right");

        left.WriteFile("logo.png", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x00, 0x01 });
        right.WriteFile("logo.png", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x00, 0x02, 0x03 });

        var harness = new ComparisonHarness();
        var job = await harness.CompareJobAsync(left.Path, right.Path);

        var node = job.Result.Root!.Require("/logo.png");
        Assert.Equal(ComparisonStatus.Modified, node.Status);
        Assert.False(node.CanCompareContent);
    }

    [Fact]
    public async Task Binary_content_behind_an_unknown_extension_is_rejected_when_the_content_is_requested()
    {
        using var left = new TempFolder("left");
        using var right = new TempFolder("right");

        left.WriteFile("blob.unknown", new byte[] { 0x01, 0x00, 0x02, 0x00, 0x03 });
        right.WriteFile("blob.unknown", new byte[] { 0x01, 0x00, 0x02, 0x00, 0x04, 0x05 });

        var harness = new ComparisonHarness();
        var job = await harness.CompareJobAsync(left.Path, right.Path);

        Assert.True(job.Result.Root!.Require("/blob.unknown").CanCompareContent);

        var content = await harness.FileContentService.GetContentAsync(job, "/blob.unknown");
        Assert.False(content.CanCompareContent);
        Assert.Contains("binary", content.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Text_behind_an_unknown_extension_is_compared_as_plain_text()
    {
        using var left = new TempFolder("left");
        using var right = new TempFolder("right");

        left.WriteFile("build.custom", "key = 1");
        right.WriteFile("build.custom", "key = 2");

        var harness = new ComparisonHarness();
        var job = await harness.CompareJobAsync(left.Path, right.Path);

        var content = await harness.FileContentService.GetContentAsync(job, "/build.custom");
        Assert.True(content.CanCompareContent);
        Assert.Equal("plaintext", content.Language);
        Assert.Equal("key = 1", content.LeftContent);
    }

    [Fact]
    public async Task Unchanged_files_are_not_offered_for_comparison()
    {
        using var left = new TempFolder("left");
        using var right = new TempFolder("right");

        left.WriteFile("same.txt", "identical");
        right.WriteFile("same.txt", "identical");

        var harness = new ComparisonHarness();
        var job = await harness.CompareJobAsync(left.Path, right.Path);

        var node = job.Result.Root!.Require("/same.txt");
        Assert.False(node.CanCompareContent);

        var content = await harness.FileContentService.GetContentAsync(job, "/same.txt");
        Assert.False(content.CanCompareContent);
    }

    [Fact]
    public async Task Empty_files_compare_as_unchanged()
    {
        using var left = new TempFolder("left");
        using var right = new TempFolder("right");

        left.WriteFile("empty.txt", Array.Empty<byte>());
        right.WriteFile("empty.txt", Array.Empty<byte>());

        var result = await new ComparisonHarness().CompareAsync(left.Path, right.Path);

        var node = result.Root!.Require("/empty.txt");
        Assert.Equal(ComparisonStatus.Unchanged, node.Status);
        Assert.Equal(0, node.LeftSize);
    }

    [Fact]
    public async Task A_file_that_becomes_empty_is_modified_and_still_diffable()
    {
        using var left = new TempFolder("left");
        using var right = new TempFolder("right");

        left.WriteFile("notes.txt", "something");
        right.WriteFile("notes.txt", Array.Empty<byte>());

        var harness = new ComparisonHarness();
        var job = await harness.CompareJobAsync(left.Path, right.Path);

        var content = await harness.FileContentService.GetContentAsync(job, "/notes.txt");
        Assert.True(content.CanCompareContent);
        Assert.Equal("something", content.LeftContent);
        Assert.Equal(string.Empty, content.RightContent);
    }

    [Fact]
    public async Task Utf8_bom_and_utf16_files_are_decoded()
    {
        using var left = new TempFolder("left");
        using var right = new TempFolder("right");

        left.WriteFile("bom.txt", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes("héllo"));
        right.WriteFile("bom.txt", new UnicodeEncoding(bigEndian: false, byteOrderMark: true).GetPreamble()
            .Concat(Encoding.Unicode.GetBytes("héllo world")).ToArray());

        var harness = new ComparisonHarness();
        var job = await harness.CompareJobAsync(left.Path, right.Path);

        var content = await harness.FileContentService.GetContentAsync(job, "/bom.txt");
        Assert.True(content.CanCompareContent);
        Assert.Equal("héllo", content.LeftContent);
        Assert.Equal("héllo world", content.RightContent);
        Assert.Equal("utf-8", content.LeftEncoding);
        Assert.Equal("utf-16", content.RightEncoding);
    }
}
