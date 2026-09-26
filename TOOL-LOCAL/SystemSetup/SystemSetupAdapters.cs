using TOOL_LOCAL.Media;
using TOOL_LOCAL.Vietsub.Ocr;
using TOOL_LOCAL.Vietsub.Translation;
using TOOL_LOCAL.Vietsub.Voice;

namespace TOOL_LOCAL.SystemSetup;

internal sealed class QwenSetupAdapter(QwenGgufVietsubTranslationProvider? provider)
    : ISetupComponentAdapter, ISetupComponentStatusInspector
{
    private static readonly VietsubTranslationComponentDefinition Definition = VietsubTranslationApprovedComponents.Qwen3_4B_Q4Km;
    public SetupComponent Component => new("qwen", "Dịch Việt · Qwen3 4B", Definition.EngineVersion,
        provider is null ? "DISABLED" : "UNKNOWN", provider is null ? "Dịch local đang tắt." : "Chưa kiểm tra trên máy này.",
        provider is not null, provider is not null, provider is not null, Definition.ModelSizeBytes, Definition.MinimumFreeDiskBytes,
        ResourceProfileId: provider?.RuntimeProfileId);
    public SetupComponent Inspect() => provider is null ? Component : Result(provider.GetRuntimeStatus());
    public async Task<SetupComponent> RunAsync(bool install, bool resourceWarningAccepted,
        Action<string, double?, long?, long?> progress, CancellationToken token)
    {
        if (provider is null) return Component;
        var reporter = new SetupProgress<VietsubTranslationRuntimeInstallProgress>(p => progress(p.Stage, p.Percent, p.BytesProcessed, p.TotalBytes));
        var before = provider.GetRuntimeStatus();
        if (install && before.Ready) return Result(before);
        if (!install && before.Status == "NOT_INSTALLED") return Result(before);
        if (install) await provider.InstallAsync(reporter, token, resourceWarningAccepted);
        else await provider.VerifyAsync(reporter, token, resourceWarningAccepted);
        return Result(provider.GetRuntimeStatus());
    }
    private SetupComponent Result(VietsubTranslationRuntimeStatus status) => Component with {
        State = status.Ready ? "READY" : status.Status == "NOT_INSTALLED" ? "NOT_INSTALLED"
            : status.ErrorCode == VietsubTranslationErrorCodes.RuntimeUnsupportedPlatform ? "UNSUPPORTED" : "NEEDS_VERIFICATION",
        Message = status.Ready ? "Đã kiểm tra dịch Anh/Trung sang Việt." : "Model chưa sẵn sàng; cần cài hoặc kiểm tra lại.",
        ErrorCode = status.ErrorCode, CheckedAtUtc = DateTime.UtcNow };
}

internal sealed class PiperSetupAdapter(VietsubVoiceComponentStore? store, bool enabled)
    : ISetupComponentAdapter, ISetupComponentStatusInspector
{
    public SetupComponent Component => new("piper", "Giọng Việt · Piper", VietsubVoiceComponentStore.RuntimeVersion,
        enabled && store is not null ? "UNKNOWN" : "DISABLED", "Giọng Việt được chuẩn bị từ gói đi kèm bộ ứng dụng.",
        enabled, enabled, enabled, 0, store?.OfflineDiskBytes ?? 768L * 1024 * 1024,
        CanPrepareOffline: enabled && store is not null && store.HasOfflineBundle(out _));
    public SetupComponent Inspect()
    {
        if (!enabled || store is null) return Component;
        var status = store.GetStatus();
        var component = Component;
        if (!status.Ready && store.UsesOfflineBundle && !store.HasOfflineBundle(out var bundleError))
            return component with { State = "REPAIR_REQUIRED", CanInstall = false,
                ErrorCode = bundleError, Message = SetupErrors.Message(bundleError!), CheckedAtUtc = DateTime.UtcNow };
        return component with {
            State = status.Ready ? "READY" : status.Status == "NOT_INSTALLED" ? "NOT_INSTALLED" : "REPAIR_REQUIRED",
            Message = status.Ready ? "Piper local đã có bằng chứng sẵn sàng trên máy này." : status.Message,
            ErrorCode = status.ErrorCode, CheckedAtUtc = DateTime.UtcNow };
    }
    public async Task<SetupComponent> RunAsync(bool install, bool resourceWarningAccepted,
        Action<string, double?, long?, long?> progress, CancellationToken token)
    {
        if (!enabled || store is null) return Component;
        var status = install
            ? await store.InstallAsync(new SetupProgress<VietsubVoiceRuntimeInstallProgress>(p =>
                progress(p.Stage, p.Percent, p.BytesProcessed, p.TotalBytes)), token)
            : await store.VerifyAsync(token);
        var inspected = Inspect();
        if (!status.Ready) return inspected;
        return inspected with { State = "READY", Message = "Đã tạo và kiểm tra WAV tiếng Việt mẫu.",
            ErrorCode = status.ErrorCode, CheckedAtUtc = DateTime.UtcNow };
    }
}

internal sealed class OcrSetupAdapter(IVietsubOcrRecognizer? recognizer, bool enabled, string? fixtureDirectory = null) : ISetupComponentAdapter
{
    public SetupComponent Component => new("ocr", "Nhận dạng phụ đề · PaddleOCR", "LocalV5 3.3.1",
        enabled ? "UNKNOWN" : "DISABLED", "Model Anh/Trung đi cùng bộ ứng dụng.", false, enabled, true, 0);
    public async Task<SetupComponent> RunAsync(bool install, bool resourceWarningAccepted,
        Action<string, double?, long?, long?> progress, CancellationToken token)
    {
        if (!enabled || recognizer is null) return Component;
        var status = recognizer is PaddleVietsubOcrRecognizer paddle
            ? await paddle.RecheckAsync(token) : await recognizer.GetRuntimeStatusAsync(token);
        if (status.Ready)
        {
            foreach (var language in new[] { "en", "zh" })
            {
                token.ThrowIfCancellationRequested();
                var path = Path.Combine(fixtureDirectory ?? Path.Combine(AppContext.BaseDirectory, "setup-fixtures"), language + ".png");
                byte[] fixture;
                try { fixture = await File.ReadAllBytesAsync(path, token); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    throw new SetupException("system_setup_ocr_fixture_invalid", "Không đọc được ảnh kiểm tra OCR trong bộ ứng dụng.");
                }
                if (fixture.Length == 0)
                    throw new SetupException("system_setup_ocr_fixture_invalid", "Ảnh kiểm tra OCR trống.");
                // OpenCV's Windows filename marshaling rejects Vietnamese path characters.
                // Read through .NET and give OpenCV image bytes, so extracted ZIP paths work.
                using var frame = OpenCvSharp.Cv2.ImDecode(fixture, OpenCvSharp.ImreadModes.Color);
                if (frame.Empty() || frame.Width != 1000 || frame.Height != 200)
                    throw new SetupException("system_setup_ocr_fixture_invalid", "Thiếu ảnh kiểm tra OCR trong bộ ứng dụng.");
                var pixels = new byte[frame.Width * frame.Height * 3];
                System.Runtime.InteropServices.Marshal.Copy(frame.Data, pixels, 0, pixels.Length);
                var result = await recognizer.RecognizeAsync(new(0, 0, frame.Width, frame.Height, pixels), language, token);
                var expected = language == "en" ? "SUBVID" : "文字幕";
                if (result.Confidence < .45f || !result.Text.Contains(expected, StringComparison.OrdinalIgnoreCase))
                    throw new SetupException("system_setup_ocr_probe_failed", "OCR chưa nhận dạng đạt ảnh kiểm tra Anh/Trung.");
            }
        }
        return Component with { State = status.Ready ? "READY" : "REPAIR_REQUIRED",
            Message = status.Ready ? "Đã nhận dạng đạt ảnh kiểm tra OCR Anh/Trung." : VietsubOcrRuntimeDiagnostics.Message(status.ErrorCode),
            ErrorCode = status.ErrorCode, CheckedAtUtc = DateTime.UtcNow };
    }
}

internal sealed class MediaSetupAdapter(IMediaToolPreflightService service, MediaToolPaths paths) : ISetupComponentAdapter
{
    public SetupComponent Component => new("media", "Xử lý video · FFmpeg", "Theo manifest bộ ứng dụng", "UNKNOWN",
        "FFmpeg và FFprobe đi cùng bộ ứng dụng.", false, true, true, 0);
    public async Task<SetupComponent> RunAsync(bool install, bool resourceWarningAccepted,
        Action<string, double?, long?, long?> progress, CancellationToken token)
    {
        if (paths.BundleDirectory is null) return Component with { State = "REPAIR_REQUIRED",
            Message = "Thiếu bộ FFmpeg đi kèm. Hãy sửa bộ ứng dụng.", ErrorCode = "media_tool_bundle_missing" };
        var status = await service.GetStatusAsync(force: true, token);
        if (status.Ready)
        {
            var root = Path.Combine(SystemSetupPaths.MetadataRoot, "probe-" + Guid.NewGuid().ToString("N"));
            SystemSetupPaths.EnsureSafeDirectory(root);
            var output = Path.Combine(root, "probe.wav");
            try
            {
                var runner = new ExternalProcessRunner();
                var execution = await runner.RunAsync(paths.FfmpegPath,
                    ["-nostdin", "-v", "error", "-f", "lavfi", "-i", "sine=frequency=440:duration=0.25", "-c:a", "pcm_s16le", output],
                    TimeSpan.FromSeconds(30), token);
                if (execution.ExitCode != 0) throw new InvalidDataException("Media probe failed.");
                var probe = await new FfprobeService(paths.FfprobePath, runner).ProbeAsync(output, token);
                if (!probe.HasAudio || probe.DurationSeconds is < .2m or > .3m)
                    throw new InvalidDataException("Media probe result failed.");
            }
            finally { if (File.Exists(output)) File.Delete(output); if (Directory.Exists(root)) Directory.Delete(root); }
        }
        return Component with { State = status.Ready ? "READY" : "REPAIR_REQUIRED",
            Message = status.Ready ? "FFmpeg/FFprobe nguyên vẹn và đã xử lý mẫu âm thanh." : "Bộ xử lý video cần được sửa qua bộ cài ứng dụng.",
            ErrorCode = status.ErrorCode, CheckedAtUtc = DateTime.UtcNow };
    }
}

// Synchronous delivery preserves native sequence order; Progress<T> posts through a context and can arrive after completion.
internal sealed class SetupProgress<T>(Action<T> action) : IProgress<T>
{
    public void Report(T value) => action(value);
}
