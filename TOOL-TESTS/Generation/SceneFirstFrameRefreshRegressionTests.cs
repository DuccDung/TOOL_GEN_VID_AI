using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.RateLimiting;
using TOOL_LOCAL.WebView;
using TOOL_SERVER.Controllers;

namespace TOOL_TESTS.Generation;

public sealed class SceneFirstFrameRefreshRegressionTests
{
    [Fact]
    public void BackgroundRefreshError_IsSerializedForTheUiAndDeduplicatedUntilRecovery()
    {
        var tracker = new BackgroundRefreshErrorTracker();

        var first = tracker.TryCreateResponse("rate_limit_exceeded", "Quá nhiều yêu cầu.");
        var duplicate = tracker.TryCreateResponse("rate_limit_exceeded", "Quá nhiều yêu cầu.");

        Assert.NotNull(first);
        Assert.Null(first.Payload);
        Assert.Equal("rate_limit_exceeded", first.Error?.Code);
        Assert.Equal("Quá nhiều yêu cầu.", first.Error?.Message);
        Assert.Null(duplicate);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(
            first,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal("rate_limit_exceeded", json.RootElement.GetProperty("error").GetProperty("code").GetString());

        tracker.MarkSuccessful();

        Assert.NotNull(tracker.TryCreateResponse("rate_limit_exceeded", "Quá nhiều yêu cầu."));
    }

    [Fact]
    public void DashboardRefresh_UsesOneProjectFirstFrameRequestInsteadOfOneRequestPerScene()
    {
        var bridge = ReadRepositoryFile("TOOL-LOCAL", "WebView", "DashboardBridge.cs");
        var refresh = Slice(bridge, "private async Task RefreshAsync(", "internal static Guid? ResolveSelectedProjectId(");
        var client = ReadRepositoryFile("TOOL-LOCAL", "Generation", "ServerGenerationClient.cs");

        Assert.Contains("GetProjectSceneFirstFramesAsync", refresh, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.WhenAll", refresh, StringComparison.Ordinal);
        Assert.Contains("api/projects/{projectId:D}/scene-first-frames", client, StringComparison.Ordinal);
    }

    [Fact]
    public void DashboardPollingEndpoints_UseTheReadOnlyRateLimitPolicy()
    {
        var frameList = typeof(SceneFirstFramesController).GetMethod(nameof(SceneFirstFramesController.ListProject));
        var providerStatus = typeof(GenerationController).GetMethod(nameof(GenerationController.GetProviderStatus));
        var program = ReadRepositoryFile("TOOL-SERVER", "Program.cs");

        Assert.Equal("ai-status", frameList?.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName);
        Assert.Equal("ai-status", providerStatus?.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName);
        Assert.Contains("options.AddPolicy(\"ai-status\"", program, StringComparison.Ordinal);
    }

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing start marker: {startMarker}");
        Assert.True(end > start, $"Missing end marker: {endMarker}");
        return source[start..end];
    }

    private static string ReadRepositoryFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(relativeParts).ToArray());
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate).Replace("\r\n", "\n", StringComparison.Ordinal);
            }
            directory = directory.Parent;
        }
        throw new FileNotFoundException($"Cannot locate repository file: {Path.Combine(relativeParts)}");
    }
}
