using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TOOL_LOCAL.Media;
using TOOL_LOCAL.Vietsub;
using TOOL_LOCAL.Vietsub.Domain;
using TOOL_LOCAL.Vietsub.Media;
using TOOL_LOCAL.Vietsub.Ocr;
using TOOL_LOCAL.Vietsub.Playback;
using TOOL_LOCAL.Vietsub.Storage;

namespace TOOL_TESTS.Vietsub;

public sealed class VietsubMediaTests
{
    [Fact]
    public async Task Copy_import_probes_hashes_atomically_and_never_changes_source()
    {
        using var workspace = new TemporaryWorkspace();
        var sourcePath = workspace.WriteFile("nguồn video.mp4", CreateBytes(2_400_000));
        var sourceBefore = await File.ReadAllBytesAsync(sourcePath);
        var project = CreateProjectManifest();
        var (service, paths, _) = CreateImportService(workspace.Root);
        paths.CreateProjectDirectories(project.ProjectId);

        var media = await service.ImportAsync(project, sourcePath, VietsubMediaImportMode.Copy);

        var sourceAfter = await File.ReadAllBytesAsync(sourcePath);
        Assert.Equal(sourceBefore, sourceAfter);
        Assert.Equal(VietsubMediaImportModes.Copy, media.ImportMode);
        Assert.Equal(12.5m, media.Metadata.DurationSeconds);
        Assert.Equal(1920, media.Metadata.Width);
        Assert.Equal(1080, media.Metadata.Height);
        Assert.True(media.Metadata.HasAudio);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(sourceBefore)).ToLowerInvariant(), media.Sha256);
        var copiedPath = paths.GetProjectPath(project.ProjectId, media.WorkspaceRelativePath!);
        Assert.Equal(sourceBefore, await File.ReadAllBytesAsync(copiedPath));
        Assert.False(File.Exists(copiedPath + ".partial"));
    }

    [Fact]
    public async Task Copy_source_remains_valid_when_only_workspace_timestamp_changes()
    {
        using var workspace = new TemporaryWorkspace();
        var sourcePath = workspace.WriteFile("copy-timestamp.mp4", CreateBytes(32_000));
        var project = CreateProjectManifest();
        var (service, paths, _) = CreateImportService(workspace.Root);
        paths.CreateProjectDirectories(project.ProjectId);
        var media = await service.ImportAsync(project, sourcePath, VietsubMediaImportMode.Copy);
        var copiedPath = paths.GetProjectPath(project.ProjectId, media.WorkspaceRelativePath!);

        File.SetLastWriteTimeUtc(copiedPath, DateTime.UtcNow.AddHours(1));
        var status = service.GetSourceStatus(project.ProjectId, media);

        Assert.True(status.Available);
        Assert.False(status.Changed);
        Assert.Null(status.IssueCode);
        Assert.Equal(Path.GetFullPath(copiedPath), status.EffectivePath);
    }

    [Fact]
    public async Task Link_import_detects_source_mutation_and_blocks_effective_path()
    {
        using var workspace = new TemporaryWorkspace();
        var sourcePath = workspace.WriteFile("linked.mp4", CreateBytes(4096));
        var project = CreateProjectManifest();
        var (service, paths, _) = CreateImportService(workspace.Root);
        paths.CreateProjectDirectories(project.ProjectId);
        var media = await service.ImportAsync(project, sourcePath, VietsubMediaImportMode.Link);

        var initial = service.GetSourceStatus(project.ProjectId, media);
        Assert.True(initial.Available);
        Assert.False(initial.Changed);
        Assert.Equal(Path.GetFullPath(sourcePath), initial.EffectivePath);

        await File.AppendAllTextAsync(sourcePath, "changed");
        File.SetLastWriteTimeUtc(sourcePath, DateTime.UtcNow.AddSeconds(2));
        var changed = service.GetSourceStatus(project.ProjectId, media);

        Assert.True(changed.Available);
        Assert.True(changed.Changed);
        Assert.Equal("vietsub_media_source_changed", changed.IssueCode);
        Assert.Null(changed.EffectivePath);
    }

    [Fact]
    public async Task OcrSourceVerification_DetectsSameSizeContentMutationBySha256()
    {
        using var workspace = new TemporaryWorkspace();
        var originalBytes = CreateBytes(4096);
        var sourcePath = workspace.WriteFile("linked-same-size.mp4", originalBytes);
        var project = CreateProjectManifest();
        var (service, paths, _) = CreateImportService(workspace.Root);
        paths.CreateProjectDirectories(project.ProjectId);
        var media = await service.ImportAsync(project, sourcePath, VietsubMediaImportMode.Link);
        var changedBytes = (byte[])originalBytes.Clone();
        changedBytes[changedBytes.Length / 2] ^= 0xff;
        await File.WriteAllBytesAsync(sourcePath, changedBytes);
        File.SetLastWriteTimeUtc(sourcePath, media.SourceLastWriteAtUtc);
        Assert.False(service.GetSourceStatus(project.ProjectId, media).Changed);

        var exception = await Assert.ThrowsAsync<VietsubOcrException>(() =>
            service.ResolveVerifiedSourcePathAsync(project.ProjectId, media));

        Assert.Equal(VietsubOcrErrorCodes.SourceChanged, exception.Code);
    }

    [Fact]
    public async Task Playback_returns_stable_recovery_code_when_linked_source_changed()
    {
        using var workspace = new TemporaryWorkspace();
        var sourcePath = workspace.WriteFile("changed-playback.mp4", CreateBytes(4096));
        var project = CreateProjectManifest();
        var (service, paths, _) = CreateImportService(workspace.Root);
        paths.CreateProjectDirectories(project.ProjectId);
        project.SourceVideo = await service.ImportAsync(project, sourcePath, VietsubMediaImportMode.Link);
        var playback = new VietsubMediaPlaybackService(service);
        var playbackUrl = VietsubMediaPlaybackService.CreatePlaybackUrl(
            project.ProjectId,
            project.SourceVideo.MediaId);

        await File.AppendAllTextAsync(sourcePath, "changed");
        File.SetLastWriteTimeUtc(sourcePath, DateTime.UtcNow.AddSeconds(2));
        var response = playback.Open(new Uri(playbackUrl), "GET", null, project);

        Assert.NotNull(response);
        Assert.Equal(409, response.StatusCode);
        Assert.Contains(
            "X-Vietsub-Error-Code: vietsub_media_source_changed",
            response.Headers,
            StringComparison.Ordinal);
        Assert.Contains(
            "X-Vietsub-Recovery-Action: relink-or-copy-source",
            response.Headers,
            StringComparison.Ordinal);
        Assert.DoesNotContain(sourcePath, response.Headers, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Cancelled_copy_removes_partial_and_destination()
    {
        using var workspace = new TemporaryWorkspace();
        var sourcePath = workspace.WriteFile("cancel.mp4", CreateBytes(8 * 1024 * 1024));
        var project = CreateProjectManifest();
        var (service, paths, _) = CreateImportService(workspace.Root);
        paths.CreateProjectDirectories(project.ProjectId);
        using var cancellation = new CancellationTokenSource();
        var progress = new InlineProgress<VietsubMediaImportProgress>(value =>
        {
            if (value.BytesProcessed > 0)
            {
                cancellation.Cancel();
            }
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ImportAsync(
                project,
                sourcePath,
                VietsubMediaImportMode.Copy,
                progress: progress,
                cancellationToken: cancellation.Token));

        var destination = paths.GetProjectPath(project.ProjectId, "source", "original.mp4");
        Assert.False(File.Exists(destination));
        Assert.False(File.Exists(destination + ".partial"));
    }

    [Fact]
    public void Copy_reference_cannot_escape_project_source_directory()
    {
        using var workspace = new TemporaryWorkspace();
        var project = CreateProjectManifest();
        var (service, paths, _) = CreateImportService(workspace.Root);
        paths.CreateProjectDirectories(project.ProjectId);
        var malicious = new VietsubMediaReference
        {
            ImportMode = VietsubMediaImportModes.Copy,
            WorkspaceRelativePath = Path.Combine("..", "outside.mp4")
        };

        Assert.ThrowsAny<Exception>(() => service.ResolveEffectivePath(project.ProjectId, malicious));
        var status = service.GetSourceStatus(project.ProjectId, malicious);
        Assert.False(status.Available);
        Assert.Equal("vietsub_media_reference_invalid", status.IssueCode);
    }

    [Theory]
    [InlineData(null, 100, 0, 99)]
    [InlineData("bytes=10-19", 100, 10, 19)]
    [InlineData("bytes=90-", 100, 90, 99)]
    [InlineData("bytes=-10", 100, 90, 99)]
    [InlineData("bytes=90-200", 100, 90, 99)]
    public void Range_parser_supports_seek_shapes(
        string? header,
        long length,
        long expectedStart,
        long expectedEnd)
    {
        Assert.True(VietsubLocalMediaRange.TryParse(header, length, out var range));
        Assert.Equal(expectedStart, range.Start);
        Assert.Equal(expectedEnd, range.End);
    }

    [Theory]
    [InlineData("items=0-1")]
    [InlineData("bytes=100-101")]
    [InlineData("bytes=10-5")]
    [InlineData("bytes=0-1,4-5")]
    public void Range_parser_rejects_unsupported_or_invalid_ranges(string header)
    {
        Assert.False(VietsubLocalMediaRange.TryParse(header, 100, out _));
    }

    [Fact]
    public async Task Playback_is_project_scoped_supports_range_and_does_not_expose_path()
    {
        using var workspace = new TemporaryWorkspace();
        var bytes = Encoding.ASCII.GetBytes("0123456789abcdefghijklmnopqrstuvwxyz");
        var sourcePath = workspace.WriteFile("range.mp4", bytes);
        var project = CreateProjectManifest();
        var (import, paths, _) = CreateImportService(workspace.Root);
        paths.CreateProjectDirectories(project.ProjectId);
        project.SourceVideo = await import.ImportAsync(project, sourcePath, VietsubMediaImportMode.Link);
        var playback = new VietsubMediaPlaybackService(import);
        var url = VietsubMediaPlaybackService.CreatePlaybackUrl(
            project.ProjectId,
            project.SourceVideo.MediaId);

        Assert.DoesNotContain(sourcePath, url, StringComparison.OrdinalIgnoreCase);
        var response = playback.Open(new Uri(url), "GET", "bytes=2-5", project);
        Assert.NotNull(response);
        Assert.Equal(206, response.StatusCode);
        Assert.Contains("Content-Range: bytes 2-5/36", response.Headers);
        using var reader = new StreamReader(response.Content, Encoding.ASCII);
        Assert.Equal("2345", await reader.ReadToEndAsync());

        var otherProjectUrl = VietsubMediaPlaybackService.CreatePlaybackUrl(
            Guid.NewGuid(),
            project.SourceVideo.MediaId);
        Assert.Equal(403, playback.Open(new Uri(otherProjectUrl), "GET", null, project).StatusCode);
        var otherMediaUrl = VietsubMediaPlaybackService.CreatePlaybackUrl(
            project.ProjectId,
            Guid.NewGuid());
        Assert.Equal(403, playback.Open(new Uri(otherMediaUrl), "GET", null, project).StatusCode);
    }

    [Fact]
    public void Playback_url_parser_rejects_traversal_query_and_non_https()
    {
        var projectId = Guid.NewGuid();
        var mediaId = Guid.NewGuid();
        Assert.False(VietsubMediaPlaybackService.TryParseUrl(
            new Uri($"https://{VietsubMediaPlaybackService.HostName}/projects/../media/{mediaId:N}"),
            out _,
            out _));
        Assert.False(VietsubMediaPlaybackService.TryParseUrl(
            new Uri(VietsubMediaPlaybackService.CreatePlaybackUrl(projectId, mediaId) + "?path=C:%5Csecret"),
            out _,
            out _));
        Assert.False(VietsubMediaPlaybackService.TryParseUrl(
            new Uri($"http://{VietsubMediaPlaybackService.HostName}/projects/{projectId:N}/media/{mediaId:N}"),
            out _,
            out _));
    }

    [Fact]
    public async Task Timeline_thumbnails_are_atomic_cached_and_project_scoped()
    {
        using var workspace = new TemporaryWorkspace();
        var sourceBytes = CreateBytes(32_000);
        var sourcePath = workspace.WriteFile("timeline.mp4", sourceBytes);
        var project = CreateProjectManifest();
        var (import, paths, runner) = CreateImportService(workspace.Root);
        paths.CreateProjectDirectories(project.ProjectId);
        project.SourceVideo = await import.ImportAsync(project, sourcePath, VietsubMediaImportMode.Link);
        var thumbnails = new VietsubTimelineThumbnailService(
            paths,
            import,
            new ReadyMediaPreflight(),
            "ffmpeg-test",
            runner);

        var first = await thumbnails.EnsureAsync(project);
        var ffmpegCallsAfterFirstPass = runner.ThumbnailCalls;
        var second = await thumbnails.EnsureAsync(project);

        Assert.Equal(VietsubTimelineThumbnailService.ThumbnailCount, first.Count);
        Assert.Equal(first, second);
        Assert.Equal(VietsubTimelineThumbnailService.ThumbnailCount, ffmpegCallsAfterFirstPass);
        Assert.Equal(ffmpegCallsAfterFirstPass, runner.ThumbnailCalls);
        Assert.All(first, url =>
        {
            Assert.DoesNotContain(workspace.Root, url, StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith($"https://{VietsubMediaPlaybackService.HostName}/projects/{project.ProjectId:N}/", url);
        });
        Assert.Empty(Directory.EnumerateFiles(
            paths.GetProjectPath(project.ProjectId, "thumbnails"),
            "*.partial.jpg",
            SearchOption.AllDirectories));
        Assert.Equal(sourceBytes, await File.ReadAllBytesAsync(sourcePath));
    }

    [Fact]
    public async Task Timeline_thumbnails_cover_the_media_with_even_time_segments()
    {
        using var workspace = new TemporaryWorkspace();
        var sourcePath = workspace.WriteFile("timeline-segments.mp4", CreateBytes(32_000));
        var project = CreateProjectManifest();
        var (import, paths, runner) = CreateImportService(workspace.Root);
        paths.CreateProjectDirectories(project.ProjectId);
        project.SourceVideo = await import.ImportAsync(project, sourcePath, VietsubMediaImportMode.Link);
        await using var thumbnails = new VietsubTimelineThumbnailService(
            paths,
            import,
            new ReadyMediaPreflight(),
            "ffmpeg-test",
            runner);

        await thumbnails.EnsureAsync(project);
        var timeline = thumbnails.GetExistingTimelineThumbnails(project);

        Assert.Equal(VietsubTimelineThumbnailService.ThumbnailCount, timeline.Count);
        Assert.Equal(0, timeline[0].StartMilliseconds);
        Assert.Equal(12_500, timeline[^1].EndMilliseconds);
        Assert.All(timeline, item =>
        {
            Assert.True(item.StartMilliseconds < item.TimestampMilliseconds);
            Assert.True(item.TimestampMilliseconds < item.EndMilliseconds);
        });
        for (var index = 1; index < timeline.Count; index++)
        {
            Assert.Equal(timeline[index - 1].EndMilliseconds, timeline[index].StartMilliseconds);
        }
    }

    [Fact]
    public async Task Thumbnail_requests_prioritize_the_latest_viewport_and_deduplicate_indices()
    {
        using var workspace = new TemporaryWorkspace();
        var sourcePath = workspace.WriteFile("priority.mp4", CreateBytes(32_000));
        var paths = new VietsubAppPaths(workspace.Root);
        var runner = new BlockingThumbnailProcessRunner();
        var import = new VietsubMediaImportService(
            paths,
            new ReadyMediaPreflight(),
            new FfprobeService("ffprobe-test", runner));
        var project = CreateProjectManifest();
        paths.CreateProjectDirectories(project.ProjectId);
        project.SourceVideo = await import.ImportAsync(project, sourcePath, VietsubMediaImportMode.Link);
        await using var thumbnails = new VietsubTimelineThumbnailService(
            paths,
            import,
            new ReadyMediaPreflight(),
            "ffmpeg-test",
            runner);
        var ready = new System.Collections.Concurrent.ConcurrentQueue<int>();
        thumbnails.ThumbnailReady += (_, item) => ready.Enqueue(item.Index);

        thumbnails.Request(project, [0, 1, 2]);
        await runner.FirstThumbnailStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        thumbnails.Request(project, [10, 2, 10]);
        runner.ReleaseFirstThumbnail.TrySetResult();
        await WaitUntilAsync(() => ready.Count == 4);

        Assert.Equal([0, 10, 2, 1], runner.ThumbnailOrder.ToArray());
        Assert.Equal([0, 10, 2, 1], ready.ToArray());
        Assert.Equal(4, runner.ThumbnailCalls);
    }

    [Fact]
    public async Task Switching_thumbnail_source_cancels_old_queue_and_suppresses_old_ready_event()
    {
        using var workspace = new TemporaryWorkspace();
        var paths = new VietsubAppPaths(workspace.Root);
        var runner = new BlockingThumbnailProcessRunner();
        var import = new VietsubMediaImportService(
            paths,
            new ReadyMediaPreflight(),
            new FfprobeService("ffprobe-test", runner));
        var firstProject = CreateProjectManifest();
        var secondProject = CreateProjectManifest();
        paths.CreateProjectDirectories(firstProject.ProjectId);
        paths.CreateProjectDirectories(secondProject.ProjectId);
        firstProject.SourceVideo = await import.ImportAsync(
            firstProject,
            workspace.WriteFile("old-source.mp4", CreateBytes(31_000)),
            VietsubMediaImportMode.Link);
        secondProject.SourceVideo = await import.ImportAsync(
            secondProject,
            workspace.WriteFile("new-source.mp4", CreateBytes(33_000)),
            VietsubMediaImportMode.Link);
        await using var thumbnails = new VietsubTimelineThumbnailService(
            paths,
            import,
            new ReadyMediaPreflight(),
            "ffmpeg-test",
            runner);
        var readyMedia = new System.Collections.Concurrent.ConcurrentQueue<Guid>();
        thumbnails.ThumbnailReady += (_, item) => readyMedia.Enqueue(item.MediaId);

        thumbnails.Request(firstProject, [0, 1]);
        await runner.FirstThumbnailStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        thumbnails.Request(secondProject, [0]);
        await WaitUntilAsync(() => readyMedia.Count == 1);

        Assert.Equal(secondProject.SourceVideo.MediaId, Assert.Single(readyMedia));
        Assert.DoesNotContain(firstProject.SourceVideo.MediaId, readyMedia);
    }

    [Fact]
    public async Task Bridge_validates_thumbnail_source_and_request_limit_before_queueing()
    {
        using var workspace = new TemporaryWorkspace();
        var paths = new VietsubAppPaths(workspace.Root);
        var store = new VietsubProjectStore(paths, new VietsubSubtitleStore(paths));
        var organizationId = Guid.NewGuid();
        const string userId = "thumbnail-request-owner";
        var manifest = await store.CreateAsync(organizationId, userId, "Thumbnail validation");
        var (import, _, runner) = CreateImportService(workspace.Root, paths);
        manifest.SourceVideo = await import.ImportAsync(
            manifest,
            workspace.WriteFile("request-validation.mp4", CreateBytes(32_000)),
            VietsubMediaImportMode.Link);
        manifest.ServerSynchronized = true;
        await store.SaveAsync(manifest);
        await using var thumbnails = new VietsubTimelineThumbnailService(
            paths,
            import,
            new ReadyMediaPreflight(),
            "ffmpeg-test",
            runner);
        var responses = new List<string>();
        using var bridge = new VietsubWebBridge(
            true,
            responses.Add,
            store,
            () => new VietsubUserContext(userId, organizationId),
            mediaImportService: import,
            thumbnailService: thumbnails);
        await bridge.TryHandleAsync(JsonSerializer.Serialize(new
        {
            type = "vietsub.project.open",
            requestId = "open-thumbnail-validation",
            payload = new { projectId = manifest.ProjectId }
        }));

        await bridge.TryHandleAsync(JsonSerializer.Serialize(new
        {
            type = "vietsub.timeline.thumbnails.request",
            requestId = "wrong-thumbnail-source",
            payload = new { sourceSha256 = new string('b', 64), indices = new[] { 0 } }
        }));
        await bridge.TryHandleAsync(JsonSerializer.Serialize(new
        {
            type = "vietsub.timeline.thumbnails.request",
            requestId = "too-many-thumbnails",
            payload = new
            {
                sourceSha256 = manifest.SourceVideo.Sha256,
                indices = Enumerable.Range(0, 65).ToArray()
            }
        }));
        await bridge.TryHandleAsync(JsonSerializer.Serialize(new
        {
            type = "vietsub.timeline.thumbnails.request",
            requestId = "invalid-thumbnail-index",
            payload = new
            {
                sourceSha256 = manifest.SourceVideo.Sha256,
                indices = new[] { 0, VietsubTimelineThumbnailService.ThumbnailCount }
            }
        }));

        var combined = string.Join('\n', responses);
        Assert.Contains("vietsub_media_artifact_stale", combined, StringComparison.Ordinal);
        Assert.Contains("vietsub_thumbnail_request_too_large", combined, StringComparison.Ordinal);
        Assert.Contains("vietsub_thumbnail_index_invalid", combined, StringComparison.Ordinal);
        Assert.Equal(0, runner.ThumbnailCalls);
    }

    [Fact]
    public async Task Source_waveform_is_real_cached_atomic_and_project_scoped()
    {
        using var workspace = new TemporaryWorkspace();
        var sourcePath = workspace.WriteFile("waveform.mp4", CreateBytes(32_000));
        var project = CreateProjectManifest();
        var (import, paths, runner) = CreateImportService(workspace.Root);
        paths.CreateProjectDirectories(project.ProjectId);
        project.SourceVideo = await import.ImportAsync(project, sourcePath, VietsubMediaImportMode.Link);
        var waveforms = new VietsubTimelineWaveformService(
            paths,
            import,
            new ReadyMediaPreflight(),
            "ffmpeg-test",
            runner);

        var first = await waveforms.EnsureAsync(project);
        var second = await waveforms.EnsureAsync(project);

        Assert.Equal(VietsubWaveformStatuses.Ready, first.Status);
        Assert.Equal(first, second);
        Assert.Equal(1, runner.WaveformCalls);
        Assert.DoesNotContain(workspace.Root, first.Url!, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFiles(
            paths.GetProjectPath(project.ProjectId, "waveforms"),
            "*.partial.png",
            SearchOption.AllDirectories));

        var playback = new VietsubMediaPlaybackService(import, waveformService: waveforms);
        var response = playback.Open(new Uri(first.Url!), "GET", null, project);
        Assert.NotNull(response);
        Assert.Equal(200, response.StatusCode);
        Assert.Contains("Content-Type: image/png", response.Headers, StringComparison.Ordinal);
        response.Content.Dispose();
        Assert.Equal(403, playback.Open(
            new Uri(VietsubMediaPlaybackService.CreateWaveformUrl(
                Guid.NewGuid(),
                project.SourceVideo.MediaId,
                project.SourceVideo.Sha256)),
            "GET",
            null,
            project).StatusCode);
    }

    [Fact]
    public async Task Source_waveform_reports_no_audio_without_running_ffmpeg()
    {
        using var workspace = new TemporaryWorkspace();
        var sourcePath = workspace.WriteFile("silent.mp4", CreateBytes(32_000));
        var project = CreateProjectManifest();
        var (import, paths, runner) = CreateImportService(workspace.Root);
        paths.CreateProjectDirectories(project.ProjectId);
        project.SourceVideo = await import.ImportAsync(project, sourcePath, VietsubMediaImportMode.Link);
        project.SourceVideo.Metadata.HasAudio = false;
        var waveforms = new VietsubTimelineWaveformService(
            paths,
            import,
            new ReadyMediaPreflight(),
            "ffmpeg-test",
            runner);

        var artifact = await waveforms.EnsureAsync(project);

        Assert.Equal(VietsubWaveformStatuses.NoAudio, artifact.Status);
        Assert.Null(artifact.Url);
        Assert.Equal(0, runner.WaveformCalls);
    }

    [Fact]
    public async Task Timeline_artifacts_return_buffered_get_and_bodyless_head_responses()
    {
        using var workspace = new TemporaryWorkspace();
        var sourcePath = workspace.WriteFile("artifact-response.mp4", CreateBytes(32_000));
        var project = CreateProjectManifest();
        var (import, paths, runner) = CreateImportService(workspace.Root);
        paths.CreateProjectDirectories(project.ProjectId);
        project.SourceVideo = await import.ImportAsync(project, sourcePath, VietsubMediaImportMode.Link);
        var thumbnails = new VietsubTimelineThumbnailService(
            paths,
            import,
            new ReadyMediaPreflight(),
            "ffmpeg-test",
            runner);
        var waveforms = new VietsubTimelineWaveformService(
            paths,
            import,
            new ReadyMediaPreflight(),
            "ffmpeg-test",
            runner);
        var thumbnailUrls = await thumbnails.EnsureAsync(project);
        var waveform = await waveforms.EnsureAsync(project);
        var playback = new VietsubMediaPlaybackService(import, thumbnails, waveforms);

        Assert.Equal(VietsubTimelineThumbnailService.ThumbnailCount, thumbnailUrls.Count);
        Assert.All(thumbnailUrls, url => Assert.Contains(
            $"/thumbnails/v{VietsubTimelineThumbnailService.ProfileVersion}/{project.SourceVideo.Sha256}/",
            url,
            StringComparison.Ordinal));
        Assert.Contains(
            $"/waveform/v{VietsubTimelineWaveformService.ProfileVersion}/{project.SourceVideo.Sha256}/source.png",
            waveform.Url!,
            StringComparison.Ordinal);
        foreach (var url in thumbnailUrls)
        {
            var response = playback.Open(new Uri(url), "GET", null, project);
            Assert.Equal(200, response.StatusCode);
            Assert.Equal(VietsubPlaybackResourceTypes.Thumbnail, response.ResourceType);
            Assert.Null(response.ErrorCode);
            Assert.Contains("Content-Type: image/jpeg", response.Headers, StringComparison.Ordinal);
            Assert.Contains("Content-Length: 256", response.Headers, StringComparison.Ordinal);
            Assert.Contains("Cross-Origin-Resource-Policy: same-site", response.Headers, StringComparison.Ordinal);
            Assert.IsType<MemoryStream>(response.Content);
            Assert.Equal(0, response.Content.Position);
            var magic = new byte[3];
            await response.Content.ReadExactlyAsync(magic);
            Assert.Equal(new byte[] { 0xff, 0xd8, 0xff }, magic);
            response.Content.Dispose();

            var head = playback.Open(new Uri(url), "HEAD", null, project);
            Assert.Equal(200, head.StatusCode);
            Assert.Contains("Content-Length: 256", head.Headers, StringComparison.Ordinal);
            Assert.Equal(0, head.Content.Length);
        }

        var waveformResponse = playback.Open(new Uri(waveform.Url!), "GET", null, project);
        Assert.Equal(200, waveformResponse.StatusCode);
        Assert.Equal(VietsubPlaybackResourceTypes.Waveform, waveformResponse.ResourceType);
        Assert.Contains("Content-Type: image/png", waveformResponse.Headers, StringComparison.Ordinal);
        var pngMagic = new byte[8];
        await waveformResponse.Content.ReadExactlyAsync(pngMagic);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }, pngMagic);
        waveformResponse.Content.Dispose();

        var waveformHead = playback.Open(new Uri(waveform.Url!), "HEAD", null, project);
        Assert.Equal(200, waveformHead.StatusCode);
        Assert.Contains("Content-Type: image/png", waveformHead.Headers, StringComparison.Ordinal);
        Assert.Equal(0, waveformHead.Content.Length);
    }

    [Fact]
    public async Task Timeline_artifact_failures_have_distinct_safe_status_and_error_codes()
    {
        using var workspace = new TemporaryWorkspace();
        var sourcePath = workspace.WriteFile("artifact-errors.mp4", CreateBytes(32_000));
        var project = CreateProjectManifest();
        var (import, paths, runner) = CreateImportService(workspace.Root);
        paths.CreateProjectDirectories(project.ProjectId);
        project.SourceVideo = await import.ImportAsync(project, sourcePath, VietsubMediaImportMode.Link);
        var thumbnails = new VietsubTimelineThumbnailService(
            paths,
            import,
            new ReadyMediaPreflight(),
            "ffmpeg-test",
            runner);
        var waveforms = new VietsubTimelineWaveformService(
            paths,
            import,
            new ReadyMediaPreflight(),
            "ffmpeg-test",
            runner);
        await thumbnails.EnsureAsync(project);
        await waveforms.EnsureAsync(project);
        var playback = new VietsubMediaPlaybackService(import, thumbnails, waveforms);

        var invalidRoute = playback.Open(
            new Uri($"https://{VietsubMediaPlaybackService.HostName}/not-a-media-route"),
            "GET",
            null,
            project);
        AssertPlaybackError(invalidRoute, 400, "vietsub_media_route_invalid", workspace.Root);

        var invalidIndex = playback.Open(
            new Uri($"https://{VietsubMediaPlaybackService.HostName}/projects/{project.ProjectId:N}/media/{project.SourceVideo.MediaId:N}/thumbnails/999.jpg"),
            "GET",
            null,
            project);
        AssertPlaybackError(invalidIndex, 400, "vietsub_media_route_invalid", workspace.Root);

        var invalidMethod = playback.Open(
            new Uri(VietsubMediaPlaybackService.CreateThumbnailUrl(
                project.ProjectId,
                project.SourceVideo.MediaId,
                project.SourceVideo.Sha256,
                0)),
            "POST",
            null,
            project);
        AssertPlaybackError(invalidMethod, 400, "vietsub_media_method_invalid", workspace.Root);

        var wrongProject = playback.Open(
            new Uri(VietsubMediaPlaybackService.CreateThumbnailUrl(
                Guid.NewGuid(),
                project.SourceVideo.MediaId,
                project.SourceVideo.Sha256,
                0)),
            "GET",
            null,
            project);
        AssertPlaybackError(wrongProject, 403, "vietsub_media_context_mismatch", workspace.Root);

        var missingPath = thumbnails.ResolveArtifactPath(project.ProjectId, project.SourceVideo.Sha256, 0)!;
        File.Delete(missingPath);
        var missing = playback.Open(
            new Uri(VietsubMediaPlaybackService.CreateThumbnailUrl(
                project.ProjectId,
                project.SourceVideo.MediaId,
                project.SourceVideo.Sha256,
                0)),
            "GET",
            null,
            project);
        AssertPlaybackError(missing, 404, "vietsub_thumbnail_artifact_missing", workspace.Root);

        var corruptPath = thumbnails.ResolveArtifactPath(project.ProjectId, project.SourceVideo.Sha256, 1)!;
        await File.WriteAllBytesAsync(corruptPath, CreateBytes(256));
        var corrupt = playback.Open(
            new Uri(VietsubMediaPlaybackService.CreateThumbnailUrl(
                project.ProjectId,
                project.SourceVideo.MediaId,
                project.SourceVideo.Sha256,
                1)),
            "GET",
            null,
            project);
        AssertPlaybackError(corrupt, 500, "vietsub_thumbnail_artifact_invalid", workspace.Root);

        var oversizedPath = thumbnails.ResolveArtifactPath(project.ProjectId, project.SourceVideo.Sha256, 4)!;
        var oversizedArtifact = new byte[VietsubMediaPlaybackService.MaximumThumbnailBytes + 1];
        new byte[] { 0xff, 0xd8, 0xff }.CopyTo(oversizedArtifact, 0);
        await File.WriteAllBytesAsync(oversizedPath, oversizedArtifact);
        var oversized = playback.Open(
            new Uri(VietsubMediaPlaybackService.CreateThumbnailUrl(
                project.ProjectId,
                project.SourceVideo.MediaId,
                project.SourceVideo.Sha256,
                4)),
            "GET",
            null,
            project);
        AssertPlaybackError(oversized, 500, "vietsub_thumbnail_artifact_invalid", workspace.Root);

        var lockedPath = thumbnails.ResolveArtifactPath(project.ProjectId, project.SourceVideo.Sha256, 2)!;
        using (new FileStream(lockedPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var unreadable = playback.Open(
                new Uri(VietsubMediaPlaybackService.CreateThumbnailUrl(
                    project.ProjectId,
                    project.SourceVideo.MediaId,
                    project.SourceVideo.Sha256,
                    2)),
                "GET",
                null,
                project);
            AssertPlaybackError(unreadable, 500, "vietsub_media_artifact_unreadable", workspace.Root);
        }

        var originalSha256 = project.SourceVideo.Sha256;
        var originalUrl = VietsubMediaPlaybackService.CreateThumbnailUrl(
            project.ProjectId,
            project.SourceVideo.MediaId,
            originalSha256,
            3);
        project.SourceVideo.Sha256 = new string('b', 64);
        var staleHash = playback.Open(
            new Uri(originalUrl),
            "GET",
            null,
            project);
        AssertPlaybackError(staleHash, 409, "vietsub_media_artifact_stale", workspace.Root);
        project.SourceVideo.Sha256 = originalSha256;

        var wrongProfileUrl = originalUrl.Replace(
            $"/thumbnails/v{VietsubTimelineThumbnailService.ProfileVersion}/",
            $"/thumbnails/v{VietsubTimelineThumbnailService.ProfileVersion + 1}/",
            StringComparison.Ordinal);
        var staleProfile = playback.Open(new Uri(wrongProfileUrl), "GET", null, project);
        AssertPlaybackError(staleProfile, 409, "vietsub_media_artifact_stale", workspace.Root);

        await File.AppendAllTextAsync(sourcePath, "changed");
        File.SetLastWriteTimeUtc(sourcePath, DateTime.UtcNow.AddSeconds(2));
        var stale = playback.Open(
            new Uri(VietsubMediaPlaybackService.CreateWaveformUrl(
                project.ProjectId,
                project.SourceVideo.MediaId,
                project.SourceVideo.Sha256)),
            "GET",
            null,
            project);
        AssertPlaybackError(stale, 409, "vietsub_media_source_changed", workspace.Root);
    }

    [Fact]
    public async Task Bridge_rejects_media_when_user_or_organization_context_changes()
    {
        using var workspace = new TemporaryWorkspace();
        var sourcePath = workspace.WriteFile("bridge-context.mp4", CreateBytes(4096));
        var paths = new VietsubAppPaths(workspace.Root);
        var store = new VietsubProjectStore(paths, new VietsubSubtitleStore(paths));
        var organizationId = Guid.NewGuid();
        const string userId = "media-owner";
        var manifest = await store.CreateAsync(organizationId, userId, "Scoped media");
        var (import, _, _) = CreateImportService(workspace.Root, paths);
        manifest.SourceVideo = await import.ImportAsync(manifest, sourcePath, VietsubMediaImportMode.Link);
        manifest.ServerSynchronized = true;
        await store.SaveAsync(manifest);
        var context = new VietsubUserContext(userId, organizationId);
        using var bridge = new VietsubWebBridge(
            true,
            _ => { },
            store,
            () => context,
            mediaImportService: import,
            mediaPlaybackService: new VietsubMediaPlaybackService(import));
        await bridge.TryHandleAsync(JsonSerializer.Serialize(new
        {
            type = "vietsub.project.open",
            requestId = "open-scoped-media",
            payload = new { projectId = manifest.ProjectId }
        }));

        var url = new Uri(VietsubMediaPlaybackService.CreatePlaybackUrl(
            manifest.ProjectId,
            manifest.SourceVideo.MediaId));
        var allowed = bridge.TryOpenPlaybackRequest(url, "GET", null);
        Assert.Equal(200, allowed.StatusCode);
        allowed.Content.Dispose();

        context = new VietsubUserContext(userId, Guid.NewGuid());
        var wrongOrganization = bridge.TryOpenPlaybackRequest(url, "GET", null);
        AssertPlaybackError(
            wrongOrganization,
            403,
            "vietsub_media_session_context_mismatch",
            workspace.Root);

        context = new VietsubUserContext("different-user", organizationId);
        var wrongUser = bridge.TryOpenPlaybackRequest(url, "GET", null);
        AssertPlaybackError(
            wrongUser,
            403,
            "vietsub_media_session_context_mismatch",
            workspace.Root);
    }

    [Fact]
    public async Task Opening_existing_project_requests_only_needed_artifacts_and_deduplicates_generation()
    {
        using var workspace = new TemporaryWorkspace();
        var sourcePath = workspace.WriteFile("reopen-artifacts.mp4", CreateBytes(32_000));
        var paths = new VietsubAppPaths(workspace.Root);
        var store = new VietsubProjectStore(paths, new VietsubSubtitleStore(paths));
        var organizationId = Guid.NewGuid();
        const string userId = "artifact-owner";
        var manifest = await store.CreateAsync(organizationId, userId, "Regenerate timeline");
        var (import, _, runner) = CreateImportService(workspace.Root, paths);
        manifest.SourceVideo = await import.ImportAsync(manifest, sourcePath, VietsubMediaImportMode.Link);
        manifest.ServerSynchronized = true;
        await store.SaveAsync(manifest);
        var thumbnails = new VietsubTimelineThumbnailService(
            paths,
            import,
            new ReadyMediaPreflight(),
            "ffmpeg-test",
            runner);
        var waveforms = new VietsubTimelineWaveformService(
            paths,
            import,
            new ReadyMediaPreflight(),
            "ffmpeg-test",
            runner);
        var responses = new System.Collections.Concurrent.ConcurrentQueue<string>();
        using var bridge = new VietsubWebBridge(
            true,
            responses.Enqueue,
            store,
            () => new VietsubUserContext(userId, organizationId),
            mediaImportService: import,
            mediaPlaybackService: new VietsubMediaPlaybackService(import, thumbnails, waveforms),
            thumbnailService: thumbnails,
            waveformService: waveforms);
        var openRequest = JsonSerializer.Serialize(new
        {
            type = "vietsub.project.open",
            requestId = "open-regenerate",
            payload = new { projectId = manifest.ProjectId }
        });

        await bridge.TryHandleAsync(openRequest);

        var states = responses
            .Select(ParseTimelineArtifactState)
            .Where(state => state is not null)
            .Select(state => state!.Value)
            .ToArray();
        Assert.Contains(states, state => state.ThumbnailCount == 0 && state.WaveformStatus == "PENDING");
        Assert.Equal(0, runner.ThumbnailCalls);
        await bridge.TryHandleAsync(JsonSerializer.Serialize(new
        {
            type = "vietsub.timeline.thumbnails.request",
            requestId = "request-one-thumbnail",
            payload = new
            {
                sourceSha256 = manifest.SourceVideo.Sha256,
                indices = new[] { 4, 4 }
            }
        }));
        await WaitUntilAsync(() => runner.ThumbnailCalls == 1 && runner.WaveformCalls == 1);
        await bridge.TryHandleAsync(
            """{"type":"vietsub.state.get","requestId":"state-after-ready","payload":{}}""");
        var readyState = responses
            .Select(ParseTimelineArtifactState)
            .Where(state => state is not null)
            .Select(state => state!.Value)
            .Last();
        Assert.Equal(1, readyState.ThumbnailCount);
        Assert.Equal(VietsubWaveformStatuses.Ready, readyState.WaveformStatus);

        await bridge.TryHandleAsync(JsonSerializer.Serialize(new
        {
            type = "vietsub.timeline.thumbnails.request",
            requestId = "request-cached-thumbnail",
            payload = new
            {
                sourceSha256 = manifest.SourceVideo.Sha256,
                indices = new[] { 4 }
            }
        }));

        Assert.Equal(1, runner.ThumbnailCalls);
        Assert.Equal(1, runner.WaveformCalls);

        await File.WriteAllBytesAsync(
            thumbnails.ResolveArtifactPath(manifest.ProjectId, manifest.SourceVideo.Sha256, 4)!,
            CreateBytes(256));
        await File.WriteAllBytesAsync(
            waveforms.ResolveArtifactPath(manifest.ProjectId, manifest.SourceVideo.Sha256)!,
            CreateBytes(256));
        responses.Clear();
        await bridge.TryHandleAsync(
            """{"type":"vietsub.state.get","requestId":"state-after-corruption","payload":{}}""");
        var invalidArtifactState = Assert.Single(responses
            .Select(ParseTimelineArtifactState)
            .Where(state => state is not null)
            .Select(state => state!.Value));
        Assert.Equal(0, invalidArtifactState.ThumbnailCount);
        Assert.Equal(VietsubWaveformStatuses.Pending, invalidArtifactState.WaveformStatus);

        await bridge.TryHandleAsync(JsonSerializer.Serialize(new
        {
            type = "vietsub.timeline.thumbnails.request",
            requestId = "request-recovery-thumbnail",
            payload = new
            {
                sourceSha256 = manifest.SourceVideo.Sha256,
                indices = new[] { 4 }
            }
        }));
        await bridge.TryHandleAsync(JsonSerializer.Serialize(new
        {
            type = "vietsub.timeline.waveform.request",
            requestId = "request-recovery-waveform",
            payload = new { sourceSha256 = manifest.SourceVideo.Sha256 }
        }));
        await WaitUntilAsync(() => runner.ThumbnailCalls == 2 && runner.WaveformCalls == 2);

        Assert.Equal(2, runner.ThumbnailCalls);
        Assert.Equal(2, runner.WaveformCalls);
    }

    [Fact]
    public async Task Bridge_state_contains_virtual_url_but_never_absolute_media_path()
    {
        using var workspace = new TemporaryWorkspace();
        var sourcePath = workspace.WriteFile("private-source.mp4", CreateBytes(4096));
        var paths = new VietsubAppPaths(workspace.Root);
        var store = new VietsubProjectStore(paths, new VietsubSubtitleStore(paths));
        var organizationId = Guid.NewGuid();
        const string userId = "vietsub-user";
        var manifest = await store.CreateAsync(organizationId, userId, "No path leak");
        var (import, _, _) = CreateImportService(workspace.Root, paths);
        manifest.SourceVideo = await import.ImportAsync(manifest, sourcePath, VietsubMediaImportMode.Link);
        manifest.Status = VietsubProjectStatuses.Ready;
        await store.SaveAsync(manifest);
        var responses = new List<string>();
        var playback = new VietsubMediaPlaybackService(import);
        using var bridge = new VietsubWebBridge(
            true,
            responses.Add,
            store,
            () => new VietsubUserContext(userId, organizationId),
            mediaImportService: import,
            mediaPlaybackService: playback);

        await bridge.TryHandleAsync(JsonSerializer.Serialize(new
        {
            type = "vietsub.project.open",
            requestId = "open-media",
            payload = new { projectId = manifest.ProjectId }
        }));

        var combined = string.Join('\n', responses);
        Assert.DoesNotContain(sourcePath, combined, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(VietsubMediaPlaybackService.HostName, combined, StringComparison.Ordinal);
    }

    private static VietsubProjectManifest CreateProjectManifest() => new()
    {
        ProjectId = Guid.NewGuid(),
        OrganizationId = Guid.NewGuid(),
        OwnerUserId = "owner",
        Name = "Media test",
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };

    private static (VietsubMediaImportService Service, VietsubAppPaths Paths, FakeMediaProcessRunner Runner)
        CreateImportService(string root, VietsubAppPaths? existingPaths = null)
    {
        var paths = existingPaths ?? new VietsubAppPaths(root);
        var runner = new FakeMediaProcessRunner();
        var service = new VietsubMediaImportService(
            paths,
            new ReadyMediaPreflight(),
            new FfprobeService("ffprobe-test", runner));
        return (service, paths, runner);
    }

    private static byte[] CreateBytes(int length)
    {
        var bytes = new byte[length];
        new Random(82917).NextBytes(bytes);
        return bytes;
    }

    private static void AssertPlaybackError(
        VietsubPlaybackResponse response,
        int expectedStatus,
        string expectedCode,
        string forbiddenPath)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal(expectedCode, response.ErrorCode);
        Assert.Contains($"X-Vietsub-Error-Code: {expectedCode}", response.Headers, StringComparison.Ordinal);
        Assert.DoesNotContain(forbiddenPath, response.Headers, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(forbiddenPath, response.ReasonPhrase, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, response.Content.Length);
    }

    private static (int ThumbnailCount, string WaveformStatus)? ParseTimelineArtifactState(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("type", out var type)
            || type.GetString() != "vietsub.state"
            || !root.TryGetProperty("payload", out var payload)
            || !payload.TryGetProperty("selectedProject", out var selectedProject)
            || selectedProject.ValueKind != JsonValueKind.Object
            || !selectedProject.TryGetProperty("sourceVideo", out var sourceVideo)
            || sourceVideo.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var thumbnailCount = sourceVideo.GetProperty("timelineThumbnails").GetArrayLength();
        var waveformStatus = sourceVideo.GetProperty("waveformStatus").GetString() ?? string.Empty;
        return (thumbnailCount, waveformStatus);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class ReadyMediaPreflight : IMediaToolPreflightService
    {
        private static readonly MediaToolStatusSummary Ready = new(
            true,
            null,
            "ready",
            "ffmpeg version test",
            "ffprobe version test",
            DateTime.UtcNow);

        public Task<MediaToolStatusSummary> GetStatusAsync(bool force, CancellationToken cancellationToken) =>
            Task.FromResult(Ready);

        public Task<MediaToolStatusSummary> RequireReadyAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Ready);
    }

    private sealed class FakeMediaProcessRunner : IExternalProcessRunner
    {
        public int ThumbnailCalls { get; private set; }

        public int WaveformCalls { get; private set; }

        public async Task<ProcessExecutionResult> RunAsync(
            string executable,
            IEnumerable<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var values = arguments.ToArray();
            if (executable.Contains("ffprobe", StringComparison.OrdinalIgnoreCase))
            {
                return new(0, """
                    {
                      "streams": [
                        { "codec_type": "video", "codec_name": "h264", "width": 1920, "height": 1080, "avg_frame_rate": "30000/1001" },
                        { "codec_type": "audio", "codec_name": "aac", "sample_rate": "48000" }
                      ],
                      "format": { "duration": "12.5" }
                    }
                    """, string.Empty);
            }

            if (values.Any(value => value.Contains("showwavespic", StringComparison.Ordinal)))
            {
                WaveformCalls++;
            }
            else
            {
                ThumbnailCalls++;
            }
            var outputPath = values[^1];
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            var artifact = CreateBytes(256);
            if (outputPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                byte[] pngMagic = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];
                pngMagic.CopyTo(artifact, 0);
            }
            else
            {
                byte[] jpegMagic = [0xff, 0xd8, 0xff];
                jpegMagic.CopyTo(artifact, 0);
            }
            await File.WriteAllBytesAsync(outputPath, artifact, cancellationToken);
            return new(0, string.Empty, string.Empty);
        }
    }

    private sealed class BlockingThumbnailProcessRunner : IExternalProcessRunner
    {
        private int _thumbnailCalls;

        public int ThumbnailCalls => Volatile.Read(ref _thumbnailCalls);

        public TaskCompletionSource FirstThumbnailStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseFirstThumbnail { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public System.Collections.Concurrent.ConcurrentQueue<int> ThumbnailOrder { get; } = new();

        public async Task<ProcessExecutionResult> RunAsync(
            string executable,
            IEnumerable<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            var values = arguments.ToArray();
            if (executable.Contains("ffprobe", StringComparison.OrdinalIgnoreCase))
            {
                return new(0, """
                    {
                      "streams": [
                        { "codec_type": "video", "codec_name": "h264", "width": 1920, "height": 1080, "avg_frame_rate": "30/1" },
                        { "codec_type": "audio", "codec_name": "aac", "sample_rate": "48000" }
                      ],
                      "format": { "duration": "12.5" }
                    }
                    """, string.Empty);
            }

            var call = Interlocked.Increment(ref _thumbnailCalls);
            var outputPath = values[^1];
            var index = int.Parse(
                Path.GetFileName(outputPath).AsSpan(0, 3),
                System.Globalization.CultureInfo.InvariantCulture);
            ThumbnailOrder.Enqueue(index);
            if (call == 1)
            {
                FirstThumbnailStarted.TrySetResult();
                await ReleaseFirstThumbnail.Task.WaitAsync(cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            var artifact = CreateBytes(256);
            new byte[] { 0xff, 0xd8, 0xff }.CopyTo(artifact, 0);
            await File.WriteAllBytesAsync(outputPath, artifact, cancellationToken);
            return new(0, string.Empty, string.Empty);
        }
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private sealed class TemporaryWorkspace : IDisposable
    {
        public TemporaryWorkspace()
        {
            Root = Path.Combine(Path.GetTempPath(), "VideoMaker-Vietsub-Media-Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public string WriteFile(string name, byte[] contents)
        {
            var path = Path.Combine(Root, name);
            File.WriteAllBytes(path, contents);
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
