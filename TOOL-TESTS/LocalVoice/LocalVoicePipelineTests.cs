using TOOL_LOCAL.LocalVoice;
using TOOL_LOCAL.Media;
using TOOL_SHARED.Contracts.Generation;

namespace TOOL_TESTS.LocalVoice;

public sealed class LocalVoicePipelineTests
{
    [Fact]
    public async Task CancelledJob_CanResumeSameCheckpoint_AndDoesNotAutoApprove()
    {
        await using var f = await LocalVoiceServiceTests.Fixture.CreateAsync(); await f.EnableAsync();
        var runtime = new Runtime { WaitForCancel = true };
        using var service = new LocalVoiceService(f.Factory, f.Store, runtime, new Media(), f.Access);
        var task = service.RunAsync(f.ProjectId, f.User, f.Org, [f.SceneId], true, null, default);
        await runtime.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(service.IsRunning);
        service.Cancel(f.ProjectId);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        var cancelled = Assert.Single(f.Store.List(f.ProjectId));
        Assert.Equal(LocalVoiceStatuses.Cancelled, cancelled.Status);
        Assert.False(service.IsRunning);
        runtime.WaitForCancel = false;
        await service.RunAsync(f.ProjectId, f.User, f.Org, [f.SceneId], true, null, default);
        var resumed = Assert.Single(f.Store.List(f.ProjectId));
        Assert.Equal(cancelled.Id, resumed.Id);
        Assert.Equal(LocalVoiceStatuses.ReviewRequired, resumed.Status);
        Assert.Null(resumed.ReviewedAtUtc);
        var calls = runtime.Calls;
        await service.RunAsync(f.ProjectId, f.User, f.Org, [f.SceneId], true, null, default);
        Assert.Equal(calls, runtime.Calls); // Idempotent while awaiting manual review.
    }

    [Fact]
    public async Task FailedConversion_RetriesLocally_AndRenderRequiresSeparateApproval()
    {
        await using var f = await LocalVoiceServiceTests.Fixture.CreateAsync(); await f.EnableAsync();
        var anchor = await f.ReviewableAsync(true); await f.ApproveAsync(anchor);
        var runtime = new Runtime { Fail = true };
        using var service = new LocalVoiceService(f.Factory, f.Store, runtime, new Media(), f.Access);
        await service.RunAsync(f.ProjectId, f.User, f.Org, [f.SceneId], false, null, default);
        var failed = Assert.Single(f.Store.List(f.ProjectId), x => !x.IsAnchor);
        Assert.Equal(LocalVoiceStatuses.Failed, failed.Status);
        runtime.Fail = false;
        await service.RunAsync(f.ProjectId, f.User, f.Org, [f.SceneId], false, null, default);
        var retried = Assert.Single(f.Store.List(f.ProjectId), x => !x.IsAnchor);
        Assert.Equal(failed.Id, retried.Id);
        Assert.Equal(LocalVoiceStatuses.ReviewRequired, retried.Status);
        Assert.Null(retried.OutputAssetId);
        await service.ReviewAsync(f.ProjectId, f.User, f.Org, retried.Id, true, true, null, default);
        Assert.NotNull(f.Store.Read(f.ProjectId, retried.Id).OutputAssetId);
        Assert.Equal(2, runtime.Calls);
    }

    [Fact]
    public async Task DuplicateBatchInput_AndConcurrentOperation_AreRejected()
    {
        await using var f = await LocalVoiceServiceTests.Fixture.CreateAsync(); await f.EnableAsync();
        var runtime = new Runtime { WaitForCancel = true };
        using var service = new LocalVoiceService(f.Factory, f.Store, runtime, new Media(), f.Access);
        await Assert.ThrowsAsync<ArgumentException>(() => service.RunAsync(f.ProjectId, f.User, f.Org, [f.SceneId, f.SceneId], false, null, default));
        var running = service.RunAsync(f.ProjectId, f.User, f.Org, [f.SceneId], true, null, default);
        await runtime.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.ThrowsAsync<ArgumentException>(() => service.RunAsync(f.ProjectId, f.User, f.Org, [f.SceneId], true, null, default));
        service.Cancel(f.ProjectId);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
    }

    private sealed class Runtime : ILocalVoiceRuntime
    {
        public bool WaitForCancel { get; set; }
        public bool Fail { get; set; }
        public int Calls { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public LocalVoiceRuntimeSummary GetStatus() => new("READY", "test", "runtime-v1");
        public Task InstallAsync(CancellationToken token) => Task.CompletedTask;
        public async Task RunAsync(string action, string work, Action<string>? progress, CancellationToken token)
        {
            Calls++; Started.TrySetResult(); progress?.Invoke(LocalVoiceStatuses.SeparatingAudio);
            if (WaitForCancel) await Task.Delay(Timeout.Infinite, token);
            if (Fail) throw new InvalidDataException("test failure");
            await File.WriteAllTextAsync(Path.Combine(work, action == "anchor" ? "anchor.wav" : "converted.wav"), "fake model result", token);
        }
    }
    private sealed class Media : ILocalVoiceMedia
    {
        public Task PrepareAsync(string video, string wav, long durationMs, CancellationToken token) => File.WriteAllTextAsync(wav, "prepared fixture", token);
        public async Task<MediaProbeResult> RemuxAsync(string video, string converted, string output, long durationMs, CancellationToken token)
        {
            await File.WriteAllTextAsync(output, "remux fixture", token);
            return new(4, 320, 180, 25, "h264", "aac", 48000, true, true);
        }
    }
}
