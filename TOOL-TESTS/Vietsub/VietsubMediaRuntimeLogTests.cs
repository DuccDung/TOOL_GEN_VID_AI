using System.Text.Json;
using TOOL_LOCAL.Vietsub.Playback;

namespace TOOL_TESTS.Vietsub;

public sealed class VietsubMediaRuntimeLogTests
{
    [Fact]
    public void Diagnostic_and_frontend_failure_payload_are_redacted()
    {
        var correlationId = Guid.NewGuid().ToString("N");
        var localPath = @"C:\Users\private-user\Videos\secret-project.mp4";
        var userId = "user-sensitive-197";
        var organizationId = Guid.NewGuid().ToString("N");
        var formatted = VietsubMediaRuntimeLog.Format(
            correlationId,
            localPath,
            "GET",
            500,
            $"{localPath}_{userId}_{organizationId}",
            "response_creation",
            nameof(IOException));
        var payload = JsonSerializer.Serialize(VietsubMediaLoadFailure.Create(
            localPath,
            correlationId,
            $"{localPath}_{userId}_{organizationId}"));

        Assert.Contains($"Correlation={correlationId}", formatted, StringComparison.Ordinal);
        Assert.Contains("Resource=unknown", formatted, StringComparison.Ordinal);
        Assert.Contains("Code=vietsub_media_unknown_error", formatted, StringComparison.Ordinal);
        Assert.DoesNotContain(localPath, formatted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(userId, formatted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(organizationId, formatted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(localPath, payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(userId, payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(organizationId, payload, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Runtime_log_rotates_at_the_configured_size()
    {
        var root = Path.Combine(Path.GetTempPath(), "VideoMaker-Media-Log-Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var logPath = Path.Combine(root, "vietsub-media-runtime.log");
        try
        {
            var log = new VietsubMediaRuntimeLog(logPath, maximumBytes: 300, retainedFiles: 1);
            for (var index = 0; index < 8; index++)
            {
                log.Write(
                    Guid.NewGuid().ToString("N"),
                    VietsubPlaybackResourceTypes.Thumbnail,
                    "GET",
                    200,
                    null,
                    "response_received");
            }

            Assert.True(File.Exists(logPath));
            Assert.True(File.Exists(logPath + ".1"));
            Assert.InRange(new FileInfo(logPath).Length, 1, 300);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
