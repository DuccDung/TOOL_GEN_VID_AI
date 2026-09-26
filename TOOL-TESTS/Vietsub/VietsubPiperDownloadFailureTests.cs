using System.Net;
using TOOL_LOCAL.Vietsub.Storage;
using TOOL_LOCAL.Vietsub.Voice;

namespace TOOL_TESTS.Vietsub;

public sealed class VietsubPiperDownloadFailureTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"vm-piper-http-{Guid.NewGuid():N}");

    [Theory]
    [InlineData("dns")]
    [InlineData("timeout")]
    [InlineData("checksum")]
    [InlineData("cancel")]
    public async Task FailedDownload_NeverBecomesReady_CleansPartialAndAllowsRetry(string failure)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var handler = new FailureHandler(failure);
        using var store = new VietsubVoiceComponentStore(new VietsubAppPaths(_root), true, handler, allowOnlineInstall: true);
        var progress = failure == "cancel" ? new CancelAfterFirstChunk(cancellation) : null;
        var error = await Record.ExceptionAsync(() => store.InstallAsync(progress, cancellation.Token));
        Assert.NotNull(error);
        if (failure is "cancel" or "timeout") Assert.IsAssignableFrom<OperationCanceledException>(error);
        else
        {
            var voiceError = Assert.IsType<VietsubVoiceException>(error);
            Assert.Equal(failure == "checksum" ? VietsubVoiceErrorCodes.RuntimeInvalid : VietsubVoiceErrorCodes.RuntimeInstallFailed,
                voiceError.Code);
            Assert.DoesNotContain("PRIVATE_TRANSPORT_DETAIL", voiceError.Message);
        }
        Assert.False(store.GetStatus().Ready);
        Assert.Empty(Directory.EnumerateFiles(_root, "*.partial", SearchOption.AllDirectories));
        Assert.Empty(Directory.EnumerateFiles(_root, ".ready.json", SearchOption.AllDirectories));
        Assert.Empty(Directory.EnumerateFiles(_root, "uv-*.zip", SearchOption.AllDirectories));
        if (progress is not null) Assert.True(progress.SawPartial);

        // A subsequent request must get past the install semaphore and reach HTTP.
        using var retryDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var retry = await Assert.ThrowsAsync<VietsubVoiceException>(() => store.InstallAsync(null, retryDeadline.Token));
        Assert.Equal(VietsubVoiceErrorCodes.RuntimeInstallFailed, retry.Code);
        Assert.Equal(2, handler.Calls);
        Assert.False(store.GetStatus().Ready);
    }

    private sealed class FailureHandler(string failure) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (++Calls > 1) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            if (failure == "dns") throw new HttpRequestException(HttpRequestError.NameResolutionError, "PRIVATE_TRANSPORT_DETAIL");
            if (failure == "timeout") throw new TaskCanceledException("PRIVATE_TRANSPORT_DETAIL");
            // Exact pinned archive size, intentionally invalid bytes: exercises SHA-256 validation.
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[19_013_455]) });
        }
    }

    private sealed class CancelAfterFirstChunk(CancellationTokenSource cancellation) : IProgress<VietsubVoiceRuntimeInstallProgress>
    {
        public bool SawPartial { get; private set; }
        public void Report(VietsubVoiceRuntimeInstallProgress value)
        {
            SawPartial = true;
            cancellation.Cancel();
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
