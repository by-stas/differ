using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FolderCompare.Api.Models;
using FolderCompare.Tests.TestSupport;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FolderCompare.Tests;

/// <summary>
/// Exercises the full REST flow against temporary folders: start a comparison, inspect the
/// result, then request file content in a shape Monaco can render.
/// </summary>
public sealed class ComparisonApiTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly HttpClient _client;
    private readonly TempFolder _left = new("api-left");
    private readonly TempFolder _right = new("api-right");

    public ComparisonApiTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Health_endpoint_reports_the_configured_limits()
    {
        var response = await _client.GetAsync("/api/health");
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("ok", document.RootElement.GetProperty("status").GetString());
        Assert.Equal(5 * 1024 * 1024, document.RootElement.GetProperty("limits").GetProperty("maxDiffFileSizeBytes").GetInt64());
    }

    [Fact]
    public async Task Full_flow_from_comparison_to_file_content()
    {
        _left.WriteFile("config.json", "{ \"mode\": \"debug\" }");
        _left.WriteFile("readme.txt", "will be removed");
        _left.WriteFile("logs/app.log", "same log");
        _left.WriteZip("package.zip", new[] { new KeyValuePair<string, string>("settings.json", "{ \"x\": 1 }") });

        _right.WriteFile("config.json", "{ \"mode\": \"release\" }");
        _right.WriteFile("new-file.txt", "added");
        _right.WriteFile("logs/app.log", "same log");
        _right.WriteZip("package.zip", new[] { new KeyValuePair<string, string>("settings.json", "{ \"x\": 2 }") });

        var result = await StartComparisonAsync();

        Assert.Equal(ComparisonState.Completed, result.Status);
        Assert.Equal(1, result.Summary!.Added);
        Assert.Equal(1, result.Summary.Removed);
        Assert.NotNull(result.Root);

        var stored = await _client.GetFromJsonAsync<ComparisonResult>($"/api/comparisons/{result.Id}", JsonOptions);
        Assert.Equal(result.Id, stored!.Id);
        Assert.Equal(ComparisonStatus.Modified, stored.Root!.Require("/config.json").Status);

        var content = await _client.GetFromJsonAsync<FileContentResult>(
            $"/api/comparisons/{result.Id}/content?path={Uri.EscapeDataString("/config.json")}",
            JsonOptions);

        Assert.True(content!.CanCompareContent);
        Assert.Equal("json", content.Language);
        Assert.Equal("{ \"mode\": \"debug\" }", content.LeftContent);
        Assert.Equal("{ \"mode\": \"release\" }", content.RightContent);

        var archiveContent = await _client.GetFromJsonAsync<FileContentResult>(
            $"/api/comparisons/{result.Id}/content?path={Uri.EscapeDataString("/package.zip!/settings.json")}",
            JsonOptions);

        Assert.True(archiveContent!.CanCompareContent);
        Assert.Equal("{ \"x\": 1 }", archiveContent.LeftContent);
        Assert.Equal("{ \"x\": 2 }", archiveContent.RightContent);
    }

    [Fact]
    public async Task Missing_folder_returns_a_structured_error()
    {
        var response = await _client.PostAsJsonAsync("/api/comparisons", new
        {
            leftPath = Path.Combine(Path.GetTempPath(), "missing-" + Guid.NewGuid().ToString("N")),
            rightPath = _right.Path
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<ApiError>(JsonOptions);
        Assert.Equal(ErrorCodes.PathNotFound, error!.Code);
        Assert.Contains("left", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Empty_paths_are_rejected_by_validation()
    {
        var response = await _client.PostAsJsonAsync("/api/comparisons", new { leftPath = "", rightPath = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiError>(JsonOptions);
        Assert.Equal(ErrorCodes.ValidationFailed, error!.Code);
    }

    [Fact]
    public async Task Unknown_comparison_ids_are_reported_as_not_found()
    {
        var response = await _client.GetAsync("/api/comparisons/cmp-does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiError>(JsonOptions);
        Assert.Equal(ErrorCodes.ComparisonNotFound, error!.Code);
    }

    [Fact]
    public async Task Requesting_a_path_outside_the_comparison_is_rejected()
    {
        _left.WriteFile("a.txt", "one");
        _right.WriteFile("a.txt", "two");

        var result = await StartComparisonAsync();

        var response = await _client.GetAsync($"/api/comparisons/{result.Id}/content?path={Uri.EscapeDataString("/does-not-exist.txt")}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<ApiError>(JsonOptions);
        Assert.Equal(ErrorCodes.NodeNotFound, error!.Code);
    }

    [Fact]
    public async Task Path_traversal_in_the_content_request_is_refused()
    {
        _left.WriteFile("a.txt", "one");
        _right.WriteFile("a.txt", "two");

        var result = await StartComparisonAsync();

        var response = await _client.GetAsync($"/api/comparisons/{result.Id}/content?path={Uri.EscapeDataString("/../../etc/passwd")}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<ApiError>(JsonOptions);
        Assert.Equal(ErrorCodes.PathInvalid, error!.Code);
    }

    [Fact]
    public async Task Content_requests_require_a_path()
    {
        _left.WriteFile("a.txt", "one");
        _right.WriteFile("a.txt", "two");

        var result = await StartComparisonAsync();

        var response = await _client.GetAsync($"/api/comparisons/{result.Id}/content");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_corrupted_archive_does_not_break_the_comparison()
    {
        _left.WriteFile("broken.zip", Encoding.UTF8.GetBytes("not a zip"));
        _right.WriteFile("broken.zip", Encoding.UTF8.GetBytes("still not a zip"));

        var result = await StartComparisonAsync();

        Assert.Equal(ComparisonState.Completed, result.Status);
        Assert.NotNull(result.Root!.Require("/broken.zip").Error);
    }

    [Fact]
    public async Task Comparisons_can_be_cancelled()
    {
        _left.WriteFile("a.txt", "one");
        _right.WriteFile("a.txt", "two");

        var result = await StartComparisonAsync();

        var response = await _client.DeleteAsync($"/api/comparisons/{result.Id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private async Task<ComparisonResult> StartComparisonAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/comparisons", new { leftPath = _left.Path, rightPath = _right.Path });
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ComparisonResult>(JsonOptions);
        Assert.NotNull(result);

        // Small fixtures finish inside the synchronous window, but poll anyway so the test is
        // not sensitive to machine speed.
        var deadline = DateTime.UtcNow.AddMinutes(1);
        while (result!.Status is ComparisonState.Pending or ComparisonState.Running && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
            result = await _client.GetFromJsonAsync<ComparisonResult>($"/api/comparisons/{result.Id}", JsonOptions);
        }

        return result!;
    }

    public void Dispose()
    {
        _left.Dispose();
        _right.Dispose();
        _client.Dispose();
    }
}
