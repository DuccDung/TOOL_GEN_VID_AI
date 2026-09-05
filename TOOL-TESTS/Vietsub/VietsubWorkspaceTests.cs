using System.Text.Json;
using Microsoft.Data.Sqlite;
using TOOL_LOCAL.Authentication;
using TOOL_LOCAL.Vietsub;
using TOOL_LOCAL.Vietsub.Api;
using TOOL_LOCAL.Vietsub.Domain;
using TOOL_LOCAL.Vietsub.Storage;
using TOOL_SHARED.Contracts.Vietsub;

namespace TOOL_TESTS.Vietsub;

public sealed class VietsubWorkspaceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"videomaker-vietsub-tiếng-việt-{Guid.NewGuid():N}");

    [Fact]
    public void Paths_UseDedicatedRoot_CreateAllDirectories_AndRejectTraversal()
    {
        var paths = new VietsubAppPaths(_root);
        var projectId = Guid.NewGuid();

        paths.CreateProjectDirectories(projectId);

        Assert.Equal(Path.Combine(Path.GetFullPath(_root), "vietsub"), paths.RootDirectory);
        Assert.Equal(
            Path.Combine(paths.RootDirectory, "projects", projectId.ToString("N")),
            paths.GetProjectDirectory(projectId));
        foreach (var directory in new[]
        {
            "source", "audio", "subtitles", "voice", "music", "cache",
            "thumbnails", "waveforms", "output", "temp", "logs"
        })
        {
            Assert.True(Directory.Exists(paths.GetProjectPath(projectId, directory)));
        }

        Assert.Throws<InvalidOperationException>(() =>
            paths.GetProjectPath(projectId, "..", "..", "outside.txt"));
        Assert.Throws<InvalidOperationException>(() =>
            paths.GetProjectPath(projectId, Path.GetPathRoot(_root)!, "outside.txt"));
    }

    [Fact]
    public async Task ProjectStore_CreateListOpenRename_EnforcesOwnerAndOrganization()
    {
        var (paths, _, store) = CreateStores();
        var organizationId = Guid.NewGuid();
        const string ownerUserId = "identity-user-1";

        var created = await store.CreateAsync(organizationId, ownerUserId, "  Dự án giới thiệu  ");
        var projects = await store.ListAsync(organizationId, ownerUserId);
        var renamed = await store.RenameAsync(
            created.ProjectId,
            organizationId,
            ownerUserId,
            "Bản dịch sản phẩm");
        var opened = await store.OpenAsync(created.ProjectId, organizationId, ownerUserId);

        Assert.Equal("Dự án giới thiệu", created.Name);
        Assert.Single(projects);
        Assert.Equal(created.ProjectId, projects[0].ProjectId);
        Assert.Equal("Bản dịch sản phẩm", renamed.Name);
        Assert.Equal("Bản dịch sản phẩm", opened.Name);
        Assert.True(File.Exists(paths.GetProjectPath(created.ProjectId, "project.json")));
        Assert.True(File.Exists(paths.GetProjectPath(created.ProjectId, "project.db")));
        Assert.Empty(await store.ListAsync(Guid.NewGuid(), ownerUserId));
        Assert.Empty(await store.ListAsync(organizationId, "identity-user-2"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            store.OpenAsync(created.ProjectId, Guid.NewGuid(), ownerUserId));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            store.OpenAsync(created.ProjectId, organizationId, "identity-user-2"));
    }

    [Fact]
    public async Task ProjectSession_UsesExclusiveLock_Autosaves_AndClosesCleanly()
    {
        var (_, _, store) = CreateStores();
        var organizationId = Guid.NewGuid();
        const string ownerUserId = "owner";
        var created = await store.CreateAsync(organizationId, ownerUserId, "Dự án khóa");
        var manifest = await store.OpenAsync(created.ProjectId, organizationId, ownerUserId);
        await using var firstSession = new VietsubProjectSession(
            store,
            manifest,
            TimeSpan.FromMilliseconds(20));
        await firstSession.StartAsync();
        Assert.False(VietsubProjectStore.ToSummary(firstSession.Manifest).NeedsRecovery);

        var competingManifest = await store.OpenAsync(created.ProjectId, organizationId, ownerUserId);
        await using var competingSession = new VietsubProjectSession(store, competingManifest);
        await Assert.ThrowsAsync<InvalidOperationException>(() => competingSession.StartAsync());

        await firstSession.UpdateAsync(project => project.Name = "Tên đã tự lưu");
        VietsubProjectManifest? autosaved = null;
        for (var attempt = 0; attempt < 100; attempt++)
        {
            autosaved = await store.OpenAsync(created.ProjectId, organizationId, ownerUserId);
            if (autosaved.Name == "Tên đã tự lưu")
            {
                break;
            }
            await Task.Delay(25);
        }

        Assert.NotNull(autosaved);
        Assert.Equal("Tên đã tự lưu", autosaved.Name);
        Assert.True(autosaved.RecoveryRequired);

        await firstSession.CloseAsync();
        var clean = await store.OpenAsync(created.ProjectId, organizationId, ownerUserId);
        Assert.False(clean.RecoveryRequired);

        await competingSession.StartAsync();
        await competingSession.CloseAsync();
    }

    [Fact]
    public async Task ProjectStore_RecoversBackupWhenCurrentManifestIsCorrupted()
    {
        var (paths, _, store) = CreateStores();
        var organizationId = Guid.NewGuid();
        const string ownerUserId = "owner";
        var created = await store.CreateAsync(organizationId, ownerUserId, "Bản đầu");
        await store.RenameAsync(created.ProjectId, organizationId, ownerUserId, "Bản mới");
        var manifestPath = paths.GetProjectPath(created.ProjectId, "project.json");
        var backupPath = manifestPath + ".bak";
        Assert.True(File.Exists(backupPath));
        File.WriteAllText(manifestPath, "{ manifest bị lỗi");

        var recovered = await store.OpenAsync(created.ProjectId, organizationId, ownerUserId);

        Assert.Equal(created.ProjectId, recovered.ProjectId);
        Assert.Equal("Bản đầu", recovered.Name);
        Assert.True(recovered.RecoveryRequired);
        Assert.True(VietsubProjectStore.ToSummary(recovered).NeedsRecovery);
        using var canonical = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath));
        Assert.Equal(created.ProjectId, canonical.RootElement.GetProperty("projectId").GetGuid());
    }

    [Fact]
    public async Task SubtitleStore_UsesWal_UpsertsIncrementally_AndDeletesStaleCues()
    {
        var (paths, subtitles, store) = CreateStores();
        var project = await store.CreateAsync(Guid.NewGuid(), "owner", "Subtitle SQLite");
        var firstCue = new VietsubSubtitleCue
        {
            StartMilliseconds = 0,
            EndMilliseconds = 1_000,
            OriginalText = "Hello"
        };
        var staleCue = new VietsubSubtitleCue
        {
            StartMilliseconds = 1_100,
            EndMilliseconds = 2_000,
            OriginalText = "World"
        };
        var track = new VietsubSubtitleTrack
        {
            DisplayName = "English",
            LanguageCode = "en",
            Source = "SRT",
            Cues = [firstCue, staleCue]
        };
        await subtitles.SaveTrackAsync(project.ProjectId, track);

        firstCue.TranslatedText = "Xin chào";
        firstCue.TranslationLocked = true;
        firstCue.UpdatedAtUtc = DateTime.UtcNow;
        track.Cues.Remove(staleCue);
        track.Revision++;
        track.UpdatedAtUtc = DateTime.UtcNow;
        await subtitles.SaveTrackAsync(project.ProjectId, track);
        var loaded = await subtitles.LoadTracksAsync(project.ProjectId);

        var savedTrack = Assert.Single(loaded);
        var savedCue = Assert.Single(savedTrack.Cues);
        Assert.Equal(2, savedTrack.Revision);
        Assert.Equal(firstCue.CueId, savedCue.CueId);
        Assert.Equal("Xin chào", savedCue.TranslatedText);
        Assert.True(savedCue.TranslationLocked);
        Assert.DoesNotContain(savedTrack.Cues, cue => cue.CueId == staleCue.CueId);

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = paths.GetProjectPath(project.ProjectId, "project.db"),
            Pooling = false
        }.ToString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var journalCommand = connection.CreateCommand();
        journalCommand.CommandText = "PRAGMA journal_mode;";
        Assert.Equal("wal", Convert.ToString(await journalCommand.ExecuteScalarAsync()));
        await using var indexCommand = connection.CreateCommand();
        indexCommand.CommandText = """
            SELECT COUNT(*) FROM sqlite_master
            WHERE type = 'index' AND name IN (
                'ux_subtitle_cues_track_order',
                'ix_subtitle_cues_timeline');
            """;
        Assert.Equal(2L, Convert.ToInt64(await indexCommand.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task SubtitleStore_InvalidBatchDoesNotOverwriteExistingTrack()
    {
        var (_, subtitles, store) = CreateStores();
        var project = await store.CreateAsync(Guid.NewGuid(), "owner", "Atomic cue batch");
        var track = new VietsubSubtitleTrack
        {
            DisplayName = "Track",
            LanguageCode = "en",
            Cues =
            [
                new VietsubSubtitleCue
                {
                    StartMilliseconds = 0,
                    EndMilliseconds = 1_000,
                    OriginalText = "Valid"
                }
            ]
        };
        await subtitles.SaveTrackAsync(project.ProjectId, track);
        track.Cues.Add(new VietsubSubtitleCue
        {
            StartMilliseconds = 2_000,
            EndMilliseconds = 1_500,
            OriginalText = "Invalid"
        });

        await Assert.ThrowsAsync<ArgumentException>(() =>
            subtitles.SaveTrackAsync(project.ProjectId, track));
        var persisted = Assert.Single(await subtitles.LoadTracksAsync(project.ProjectId));
        Assert.Single(persisted.Cues);
        Assert.Equal("Valid", persisted.Cues[0].OriginalText);
    }

    [Fact]
    public async Task Bridge_ProjectCreateAndRename_ReturnsOnlyMetadataWithoutLocalPaths()
    {
        var (_, _, store) = CreateStores();
        var responses = new List<string>();
        var organizationId = Guid.NewGuid();
        var registry = new FakeRegistryClient();
        using var bridge = new VietsubWebBridge(
            enabled: true,
            responses.Add,
            store,
            () => new VietsubUserContext("owner", organizationId),
            registry);

        await bridge.TryHandleAsync(
            """{"type":"vietsub.project.create","requestId":"create-1","payload":{"name":"Dự án qua bridge"}}""");
        using var createdState = JsonDocument.Parse(responses[^1]);
        var selected = createdState.RootElement.GetProperty("payload").GetProperty("selectedProject");
        var projectId = selected.GetProperty("projectId").GetGuid();
        Assert.Equal("Dự án qua bridge", selected.GetProperty("name").GetString());
        Assert.True(selected.GetProperty("serverSynchronized").GetBoolean());
        Assert.Equal(1, registry.RegisterCalls);
        Assert.DoesNotContain(_root, responses[^1], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("project.db", responses[^1], StringComparison.OrdinalIgnoreCase);

        responses.Clear();
        var renameRequest = JsonSerializer.Serialize(new
        {
            type = "vietsub.project.rename",
            requestId = "rename-1",
            payload = new { projectId, name = "Tên mới" }
        });
        await bridge.TryHandleAsync(renameRequest);
        using var renamedState = JsonDocument.Parse(responses[^1]);
        Assert.Equal(
            "Tên mới",
            renamedState.RootElement.GetProperty("payload").GetProperty("selectedProject").GetProperty("name").GetString());
        Assert.True(
            renamedState.RootElement.GetProperty("payload").GetProperty("selectedProject").GetProperty("serverSynchronized").GetBoolean());
        Assert.Equal(2, registry.RegisterCalls);
        Assert.Equal(1, registry.RenameCalls);
    }

    private (VietsubAppPaths Paths, VietsubSubtitleStore Subtitles, VietsubProjectStore Projects) CreateStores()
    {
        var paths = new VietsubAppPaths(_root);
        var subtitles = new VietsubSubtitleStore(paths);
        return (paths, subtitles, new VietsubProjectStore(paths, subtitles));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class FakeRegistryClient : IVietsubProjectRegistryClient
    {
        private string? _registeredName;

        public int RegisterCalls { get; private set; }

        public int RenameCalls { get; private set; }

        public Task<IReadOnlyList<VietsubProjectResponse>> ListAsync(
            Guid organizationId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<VietsubProjectResponse>>([]);

        public Task<VietsubProjectResponse> RegisterAsync(
            VietsubProjectManifest manifest,
            CancellationToken cancellationToken)
        {
            RegisterCalls++;
            if (_registeredName is not null && _registeredName != manifest.Name)
            {
                throw new AccountClientException(
                    "vietsub_project_id_conflict",
                    "conflict",
                    409);
            }

            _registeredName = manifest.Name;
            return Task.FromResult(Response(manifest.ProjectId, manifest.OrganizationId, manifest.Name));
        }

        public Task<VietsubProjectResponse> RenameAsync(
            Guid projectId,
            Guid organizationId,
            string name,
            CancellationToken cancellationToken)
        {
            RenameCalls++;
            _registeredName = name;
            return Task.FromResult(Response(projectId, organizationId, name));
        }

        public Task<VietsubProjectResponse> ArchiveAsync(
            Guid projectId,
            Guid organizationId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Response(projectId, organizationId, _registeredName ?? "Project") with
            {
                IsArchived = true,
                ArchivedAtUtc = DateTime.UtcNow
            });

        private static VietsubProjectResponse Response(
            Guid projectId,
            Guid organizationId,
            string name) =>
            new(
                projectId,
                organizationId,
                "owner",
                name,
                "DRAFT",
                "auto",
                "vi",
                false,
                DateTime.UtcNow,
                DateTime.UtcNow,
                null);
    }
}
