using System.Reflection;
using Microsoft.AspNetCore.RateLimiting;
using TOOL_SERVER.Controllers;

namespace TOOL_SERVER_TESTS;

public sealed class SceneFirstFrameRateLimitTests
{
    [Fact]
    public void DashboardPollingEndpoints_UseTheReadOnlyRateLimitPolicy()
    {
        var frameList = typeof(SceneFirstFramesController).GetMethod(nameof(SceneFirstFramesController.ListProject));
        var providerStatus = typeof(GenerationController).GetMethod(nameof(GenerationController.GetProviderStatus));
        var program = ReadRepositoryFile("TOOL-SERVER", "Program.cs");

        Assert.Equal("ai-status", frameList?.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName);
        Assert.Equal("ai-status", providerStatus?.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName);
        Assert.Contains("options.AddPolicy(\"ai-status\"", program, StringComparison.Ordinal);
        foreach (var method in new[] { "GetLatestContentLanguageFailure", "GetVideoStatus", "DownloadVideo", "GetKlingVideoStatus", "DownloadKlingVideo" })
            Assert.Equal("ai-status", typeof(GenerationController).GetMethod(method)?.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName);
        foreach (var method in new[] { "Get", "Image" })
            Assert.Equal("ai-status", typeof(ShortVideoOutfitController).GetMethod(method)?.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName);
        Assert.Equal("ai-gateway", typeof(ShortVideoOutfitController).GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName);
    }

    private static string ReadRepositoryFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(relativeParts).ToArray());
            if (File.Exists(candidate))
                return File.ReadAllText(candidate).Replace("\r\n", "\n", StringComparison.Ordinal);
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Cannot locate repository file: {Path.Combine(relativeParts)}");
    }
}
