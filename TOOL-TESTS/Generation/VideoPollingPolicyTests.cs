using TOOL_SERVER.Generation;

namespace TOOL_TESTS.Generation;

public sealed class VideoPollingPolicyTests
{
    private static readonly VideoPollingOptions Options = new()
    {
        MaximumAttempts = 3000,
        MaximumAgeHours = 72
    };

    [Fact]
    public void RetryPolicy_AllowsRetryBeforeBothLimits()
    {
        var now = DateTime.UtcNow;

        Assert.False(VideoPollingPolicy.ReachedTerminalLimit(2999, now.AddHours(-71), now, Options));
    }

    [Fact]
    public void RetryPolicy_StopsAtMaximumAttempts()
    {
        var now = DateTime.UtcNow;

        Assert.True(VideoPollingPolicy.ReachedTerminalLimit(3000, now.AddHours(-1), now, Options));
    }

    [Fact]
    public void RetryPolicy_StopsAtMaximumTaskAge()
    {
        var now = DateTime.UtcNow;

        Assert.True(VideoPollingPolicy.ReachedTerminalLimit(1, now.AddHours(-72), now, Options));
    }

    [Fact]
    public void PollingOwnership_IsCentralizedInWorkerWithAnAtomicLeaseClaim()
    {
        var worker = ReadRepositoryFile("TOOL-SERVER", "Generation", "KlingPollingWorker.cs");
        var service = ReadRepositoryFile("TOOL-SERVER", "Generation", "GenerationService.cs");

        Assert.Contains("ExecuteUpdateAsync", worker, StringComparison.Ordinal);
        Assert.Contains("ClaimLeaseMinutes", worker, StringComparison.Ordinal);
        Assert.Contains("x.ProviderCode == ProviderCodes.Fal", worker, StringComparison.Ordinal);
        Assert.Equal(
            1,
            worker.Split(".GetStatusAsync(", StringSplitOptions.None).Length - 1);
        Assert.Contains("background worker is the single polling owner", service, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RetryError_DistinguishesProviderStatusFromCompletedOutputCache()
    {
        var statusFailure = VideoPollingPolicy.RetryError(providerReportedCompleted: false);
        var outputFailure = VideoPollingPolicy.RetryError(providerReportedCompleted: true);

        Assert.Equal("provider_status_check_failed", statusFailure.ErrorCode);
        Assert.Equal("provider_output_download_failed", outputFailure.ErrorCode);
        Assert.Contains("hoàn tất", outputFailure.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("URL", outputFailure.ErrorMessage, StringComparison.OrdinalIgnoreCase);
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
