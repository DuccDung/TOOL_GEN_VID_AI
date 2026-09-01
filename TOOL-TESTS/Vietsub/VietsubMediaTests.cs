using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TOOL_LOCAL.Media;
using TOOL_LOCAL.Vietsub;
using TOOL_LOCAL.Vietsub.Domain;
using TOOL_LOCAL.Vietsub.Media;
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
        Assert.Null(playback.Open(new Uri(otherProjectUrl), "GET", null, project));
        var otherMediaUrl = VietsubMediaPlaybackService.CreatePlaybackUrl(
            project.ProjectId,
            Guid.NewGuid());
        Assert.Null(playback.Open(new Uri(otherMediaUrl), "GET", null, project));
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

            ThumbnailCalls++;
            var outputPath = values[^1];
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            await File.WriteAllBytesAsync(outputPath, CreateBytes(256), cancellationToken);
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
