using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TOOL_LOCAL.Data;
using TOOL_LOCAL.Data.Models;
using TOOL_LOCAL.Generation;
using TOOL_SHARED.Contracts.Generation;

namespace TOOL_LOCAL.LocalVoice;

internal sealed class LocalVoiceService(IDbContextFactory<VideoFactoryDbContext> factory,
    LocalVoiceStore store, ILocalVoiceRuntime runtime, ILocalVoiceMedia media, ILocalVoiceAccessClient client) : IDisposable
{
    private readonly SemaphoreSlim _operations = new(1, 1);
    private CancellationTokenSource? _active;
    private Guid? _activeProject;
    private Guid? _activeJob;
    public bool IsRunning => _active is not null;

    public async Task<LocalVoiceProjectSummary> GetAsync(Guid projectId, string userId, Guid organizationId, CancellationToken token)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        var project = await RequireProjectAsync(db, projectId, userId, organizationId, token);
        var anchors = new List<LocalVoiceAnchorSummary>();
        var jobs = new List<LocalVoiceJobSummary>();
        foreach (var record in store.List(projectId))
        {
            if (record.Source.OwnerId != userId || record.Source.OrganizationId != organizationId) continue;
            var current = await IsCurrentAsync(db, project, record, token);
            var status = current ? record.Status : LocalVoiceStatuses.Stale;
            if (LocalVoiceStatuses.IsRunning(status) && _activeJob != record.Id) status = "Interrupted";
            var preview = current && record.OutputRelativePath is not null && record.OutputSha256 is not null
                ? await PreviewAsync(record, token) : null;
            if (record.IsAnchor) anchors.Add(new(record.Id, record.Source.CharacterId, record.Source.SceneId, status, preview, record.Message));
            else jobs.Add(new(record.Id, record.Source.SceneId, record.Source.CharacterId, status, record.ErrorCode,
                record.Message, preview, record.AnchorId, record.Source.Fingerprint,
                current ? await SourcePreviewAsync(record.Source, token) : null, record.NativeException));
        }
        return new(projectId, project.LocalVoicePolicyVersion == LocalVoicePolicies.VeoLocalVoiceConsistency,
            runtime.GetStatus(), anchors, jobs, _activeProject == projectId);
    }

    public async Task EnableAsync(Guid projectId, string userId, Guid organizationId, bool confirmed, CancellationToken token)
    {
        RequireConfirmed(confirmed);
        if (runtime.GetStatus().Status == "DISABLED") throw new ArgumentException("Hãy bật VeoLocalVoiceConsistencyEnabled trong cấu hình desktop rồi khởi động lại.");
        await AuthorizeAsync(projectId, organizationId, token);
        await _operations.WaitAsync(token);
        try
        {
            using var projectLock = store.AcquireProjectLock(projectId);
            await using var db = await factory.CreateDbContextAsync(token);
            var project = await RequireProjectAsync(db, projectId, userId, organizationId, token);
            await RequireApplicableAsync(db, project, token);
            if (project.LocalVoicePolicyVersion == LocalVoicePolicies.VeoLocalVoiceConsistency) return;
            project.LocalVoicePolicyVersion = LocalVoicePolicies.VeoLocalVoiceConsistency;
            project.UpdatedAtUtc = DateTime.UtcNow;
            // Native approval remains evidence for the source, but it no longer selects final audio.
            var scenes = await db.Scenes.Where(x => x.ProjectId == projectId && x.Dialogue != null && x.Dialogue != "").ToListAsync(token);
            foreach (var scene in scenes) scene.ApprovedRenderMediaAssetId = null;
            await db.SaveChangesAsync(token);
        }
        finally { _operations.Release(); }
    }

    public async Task InstallAsync(Guid projectId, Guid organizationId, bool confirmed, CancellationToken token)
    {
        RequireConfirmed(confirmed);
        await AuthorizeAsync(projectId, organizationId, token);
        await _operations.WaitAsync(token);
        using var execution = CancellationTokenSource.CreateLinkedTokenSource(token);
        _active = execution; _activeProject = projectId;
        try { await runtime.InstallAsync(execution.Token); }
        finally { _active = null; _activeProject = null; _operations.Release(); }
    }

    public async Task RunAsync(Guid projectId, string userId, Guid organizationId, IReadOnlyList<Guid> sceneIds,
        bool anchor, Action? changed, CancellationToken token)
    {
        if (sceneIds.Count is < 1 or > 100 || sceneIds.Distinct().Count() != sceneIds.Count || anchor && sceneIds.Count != 1)
            throw new ArgumentException("Chọn từ 1 đến 100 cảnh khác nhau; mỗi lần chỉ chuẩn bị một mẫu giọng.");
        await AuthorizeAsync(projectId, organizationId, token);
        if (runtime.GetStatus() is not { Status: "READY", Fingerprint: not null } ready)
            throw new ArgumentException("Runtime chưa sẵn sàng. Hãy cài hoặc kiểm tra lại.");
        if (!await _operations.WaitAsync(0, token)) throw new ArgumentException("Đang có thao tác giọng local; hãy chờ hoặc hủy.");
        using var execution = CancellationTokenSource.CreateLinkedTokenSource(token);
        _active = execution; _activeProject = projectId;
        try
        {
            using var projectLock = store.AcquireProjectLock(projectId);
            foreach (var sceneId in sceneIds)
            {
                execution.Token.ThrowIfCancellationRequested();
                await AuthorizeAsync(projectId, organizationId, execution.Token);
                await using var db = await factory.CreateDbContextAsync(execution.Token);
                var project = await RequireProjectAsync(db, projectId, userId, organizationId, execution.Token);
                if (project.LocalVoicePolicyVersion != LocalVoicePolicies.VeoLocalVoiceConsistency) throw new ArgumentException("Project chưa bật đồng nhất giọng.");
                var source = await SourceAsync(db, project, sceneId, execution.Token);
                var records = store.List(projectId);
                LocalVoiceRecord? reference = null;
                if (!anchor)
                {
                    reference = records.FirstOrDefault(x => x.IsAnchor && x.Source.CharacterId == source.CharacterId && x.Status == LocalVoiceStatuses.Approved);
                    if (reference is null || !await IsCurrentAsync(db, project, reference, execution.Token))
                        throw new ArgumentException("Nhân vật chưa có mẫu giọng được duyệt và còn hiệu lực.");
                    await store.RequireHashAsync(reference.OutputRelativePath!, reference.OutputSha256!, execution.Token);
                }
                var duplicate = records.FirstOrDefault(x => x.IsAnchor == anchor && !x.NativeException &&
                    x.Source.Fingerprint == source.Fingerprint && x.RuntimeFingerprint == ready.Fingerprint &&
                    x.AnchorFingerprint == reference?.Fingerprint &&
                    x.Status is LocalVoiceStatuses.ReviewRequired or LocalVoiceStatuses.Approved);
                if (duplicate is not null && duplicate.OutputRelativePath is not null)
                {
                    await store.RequireHashAsync(duplicate.OutputRelativePath, duplicate.OutputSha256!, execution.Token);
                    continue;
                }
                var record = records.FirstOrDefault(x => x.IsAnchor == anchor && !x.NativeException &&
                    x.Source.Fingerprint == source.Fingerprint && x.RuntimeFingerprint == ready.Fingerprint &&
                    x.AnchorFingerprint == reference?.Fingerprint && (x.Status is LocalVoiceStatuses.Failed or LocalVoiceStatuses.Cancelled || LocalVoiceStatuses.IsRunning(x.Status)))
                    ?? new LocalVoiceRecord { IsAnchor = anchor, Source = source,
                    AnchorId = reference?.Id, AnchorFingerprint = reference?.Fingerprint, RuntimeFingerprint = ready.Fingerprint };
                _activeJob = record.Id;
                record.Status = LocalVoiceStatuses.Preparing; record.ErrorCode = null; record.Message = null;
                record.OutputRelativePath = null; record.OutputSha256 = null;
                store.Save(record); changed?.Invoke();
                var directoryRelative = store.JobRelative(projectId, record.Id);
                var work = Path.GetDirectoryName(store.Resolve(directoryRelative + "/source.wav"))!;
                Directory.CreateDirectory(work);
                try
                {
                    await store.RequireHashAsync(source.RelativePath, source.Sha256, execution.Token);
                    await media.PrepareAsync(store.Resolve(source.RelativePath), Path.Combine(work, "source.wav"), source.DurationMs, execution.Token);
                    if (reference is not null) File.Copy(store.Resolve(reference.OutputRelativePath!), Path.Combine(work, "anchor.wav"), true);
                    await runtime.RunAsync(anchor ? "anchor" : "convert", work, stage => {
                        if (!LocalVoiceStatuses.IsRunning(stage)) throw new InvalidDataException("Worker stage không hợp lệ.");
                        record.Status = stage; store.Save(record); changed?.Invoke();
                    }, execution.Token);
                    record.Status = LocalVoiceStatuses.Validating; store.Save(record); changed?.Invoke();
                    var output = anchor ? "anchor.wav" : "converted.mp4";
                    if (!anchor) await media.RemuxAsync(store.Resolve(source.RelativePath), Path.Combine(work, "converted.wav"),
                        Path.Combine(work, output), source.DurationMs, execution.Token);
                    record.OutputRelativePath = directoryRelative + "/" + output;
                    record.OutputSha256 = await LocalVoiceStore.FileHashAsync(store.Resolve(record.OutputRelativePath), execution.Token);
                    await AuthorizeAsync(projectId, organizationId, execution.Token);
                    await using var currentDb = await factory.CreateDbContextAsync(execution.Token);
                    var currentProject = await RequireProjectAsync(currentDb, projectId, userId, organizationId, execution.Token);
                    if (!await IsCurrentAsync(currentDb, currentProject, record, execution.Token))
                        throw new InvalidDataException("Nguồn hoặc mẫu giọng đã thay đổi trong lúc xử lý.");
                    await store.RequireHashAsync(source.RelativePath, source.Sha256, execution.Token);
                    record.Status = LocalVoiceStatuses.ReviewRequired;
                    record.Message = "Hãy nghe so sánh câu chữ, giọng, khẩu hình và âm nền trước khi duyệt.";
                }
                catch (OperationCanceledException)
                { record.Status = LocalVoiceStatuses.Cancelled; record.Message = "Đã hủy. Clip gốc được giữ để chạy lại."; throw; }
                catch (Exception e) when (e is IOException or InvalidDataException or InvalidOperationException or ArgumentException or JsonException)
                { record.Status = LocalVoiceStatuses.Failed; record.ErrorCode = "local_voice_failed"; record.Message = "Xử lý không đạt. Kiểm tra nguồn, mẫu giọng hoặc runtime rồi chạy lại."; }
                finally { store.Save(record); changed?.Invoke(); }
            }
        }
        finally { _active = null; _activeProject = null; _activeJob = null; _operations.Release(); }
    }

    public void Cancel(Guid projectId) { if (_activeProject == projectId) _active?.Cancel(); }

    public async Task<int> CleanupAsync(Guid projectId, string userId, Guid org, bool confirmed, CancellationToken token)
    {
        RequireConfirmed(confirmed);
        await AuthorizeAsync(projectId, org, token);
        await _operations.WaitAsync(token);
        try
        {
            using var projectLock = store.AcquireProjectLock(projectId);
            await using var db = await factory.CreateDbContextAsync(token);
            await RequireProjectAsync(db, projectId, userId, org, token);
            var removed = 0;
            foreach (var record in store.List(projectId).Where(x => x.Source.OwnerId == userId && x.Source.OrganizationId == org &&
                x.Status is LocalVoiceStatuses.Approved or LocalVoiceStatuses.Rejected or LocalVoiceStatuses.Stale))
            {
                // Explicit intermediate allowlist only; never delete native, approved anchor, output or checkpoint.
                foreach (var name in new[] { "source.wav", "speech.wav", "residual.wav", "windows.json", "prepared.json", "conversion.json", "converted.wav" })
                {
                    token.ThrowIfCancellationRequested();
                    var relative = store.JobRelative(projectId, record.Id) + "/" + name;
                    if (relative == record.OutputRelativePath || relative == record.Source.RelativePath) continue;
                    var path = store.Resolve(relative);
                    if (File.Exists(path)) { File.Delete(path); removed++; }
                }
            }
            return removed;
        }
        finally { _operations.Release(); }
    }

    public async Task<MediaAsset?> ResolveRenderAsync(VideoFactoryDbContext db, Project project, Scene scene, CancellationToken token)
    {
        var record = store.List(project.ProjectId).FirstOrDefault(x => !x.IsAnchor && x.Source.SceneId == scene.SceneId &&
            x.Status == LocalVoiceStatuses.Approved && x.OutputAssetId == scene.ApprovedRenderMediaAssetId);
        if (record is null || record.ReviewedAtUtc is null || record.ReviewedBy != project.RemoteUserId ||
            record.NativeException && string.IsNullOrWhiteSpace(record.ReviewReason) ||
            !await IsCurrentAsync(db, project, record, token)) return null;
        await store.RequireHashAsync(record.Source.RelativePath, record.Source.Sha256, token);
        await store.RequireHashAsync(record.OutputRelativePath!, record.OutputSha256!, token);
        if (record.AnchorId is { } anchorId)
        {
            var anchor = store.Read(project.ProjectId, anchorId);
            await store.RequireHashAsync(anchor.Source.RelativePath, anchor.Source.Sha256, token);
            await store.RequireHashAsync(anchor.OutputRelativePath!, anchor.OutputSha256!, token);
        }
        return await db.MediaAssets.AsNoTracking().SingleOrDefaultAsync(x => x.MediaAssetId == record.OutputAssetId &&
            x.ProjectId == project.ProjectId && x.SceneId == scene.SceneId && x.Sha256 == record.OutputSha256 &&
            x.Status == "Ready" && x.DeletedAtUtc == null, token);
    }

    public string? GetRenderApprovalFingerprint(Guid projectId, Guid sceneId, Guid? outputAssetId) =>
        store.List(projectId).FirstOrDefault(x => !x.IsAnchor && x.Source.SceneId == sceneId &&
            x.Status == LocalVoiceStatuses.Approved && x.OutputAssetId == outputAssetId)?.Fingerprint;

    public async Task ReviewAsync(Guid projectId, string userId, Guid organizationId, Guid id,
        bool approve, bool confirmed, string? reason, CancellationToken token)
    {
        RequireConfirmed(confirmed);
        await AuthorizeAsync(projectId, organizationId, token);
        await _operations.WaitAsync(token);
        try
        {
            using var projectLock = store.AcquireProjectLock(projectId);
            await using var db = await factory.CreateDbContextAsync(token);
            var project = await RequireProjectAsync(db, projectId, userId, organizationId, token);
            var record = store.Read(projectId, id);
            if (record.Source.OwnerId != userId || record.Source.OrganizationId != organizationId ||
                !await IsCurrentAsync(db, project, record, token)) throw new ArgumentException("Kết quả đã hết hiệu lực.");
            if (record.Status != LocalVoiceStatuses.ReviewRequired) throw new ArgumentException("Kết quả không ở trạng thái chờ duyệt.");
            await store.RequireHashAsync(record.Source.RelativePath, record.Source.Sha256, token);
            await store.RequireHashAsync(record.OutputRelativePath!, record.OutputSha256!, token);
            if (record.AnchorId is { } referenceId)
            {
                var reference = store.Read(projectId, referenceId);
                await store.RequireHashAsync(reference.Source.RelativePath, reference.Source.Sha256, token);
                await store.RequireHashAsync(reference.OutputRelativePath!, reference.OutputSha256!, token);
            }
            record.ReviewedBy = userId; record.ReviewedAtUtc = DateTime.UtcNow;
            record.ReviewReason = SafeReason(reason);
            record.Status = approve ? LocalVoiceStatuses.Approved : LocalVoiceStatuses.Rejected;
            if (approve && record.IsAnchor)
            {
                foreach (var previous in store.List(projectId).Where(x => x.Id != id && x.IsAnchor && x.Source.CharacterId == record.Source.CharacterId && x.Status == LocalVoiceStatuses.Approved))
                { previous.Status = LocalVoiceStatuses.Stale; store.Save(previous); }
                var affected = await db.Scenes.Where(x => x.ProjectId == projectId && x.CharacterIdsJson != null).ToListAsync(token);
                foreach (var scene in affected.Where(x => ParseCharacterIds(x.CharacterIdsJson).Contains(record.Source.CharacterId)))
                    scene.ApprovedRenderMediaAssetId = null;
            }
            if (approve && !record.IsAnchor) await MaterializeAsync(db, project, record, token);
            await db.SaveChangesAsync(token);
            // A crash before this atomic write leaves a non-approved checkpoint, so render fails closed.
            store.Save(record);
        }
        finally { _operations.Release(); }
    }

    public async Task UseNativeAsync(Guid projectId, string userId, Guid organizationId, Guid sceneId,
        bool confirmed, string? reason, CancellationToken token)
    {
        RequireConfirmed(confirmed);
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Cần ghi lý do sử dụng bản native.");
        await AuthorizeAsync(projectId, organizationId, token);
        await _operations.WaitAsync(token);
        try
        {
            using var projectLock = store.AcquireProjectLock(projectId);
            await using var db = await factory.CreateDbContextAsync(token);
            var project = await RequireProjectAsync(db, projectId, userId, organizationId, token);
            var source = await SourceAsync(db, project, sceneId, token);
            if (project.LocalVoicePolicyVersion != LocalVoicePolicies.VeoLocalVoiceConsistency)
                throw new ArgumentException("Project chưa bật đồng nhất giọng local.");
            await store.RequireHashAsync(source.RelativePath, source.Sha256, token);
            var record = new LocalVoiceRecord { Source = source, NativeException = true, Status = LocalVoiceStatuses.Approved,
                OutputAssetId = source.MediaAssetId, OutputRelativePath = source.RelativePath, OutputSha256 = source.Sha256,
                ReviewedBy = userId, ReviewedAtUtc = DateTime.UtcNow, ReviewReason = SafeReason(reason) };
            var scene = await db.Scenes.SingleAsync(x => x.SceneId == sceneId, token);
            scene.ApprovedRenderMediaAssetId = source.MediaAssetId;
            await db.SaveChangesAsync(token);
            store.Save(record);
        }
        finally { _operations.Release(); }
    }

    private async Task MaterializeAsync(VideoFactoryDbContext db, Project project, LocalVoiceRecord record, CancellationToken token)
    {
        var relative = Path.GetRelativePath(Path.GetDirectoryName(store.Resolve(project.WorkspaceRelativePath + "/placeholder"))!,
            store.Resolve(record.OutputRelativePath!)).Replace('\\', '/');
        var id = Guid.NewGuid();
        db.MediaAssets.Add(new MediaAsset { MediaAssetId = id, ProjectId = project.ProjectId, SceneId = record.Source.SceneId,
            AssetType = LocalVoicePolicies.AssetType, DisplayName = "Clip đã đồng nhất giọng", RelativePath = relative,
            MimeType = "video/mp4", SizeBytes = new FileInfo(store.Resolve(record.OutputRelativePath!)).Length,
            Sha256 = record.OutputSha256!, DurationMs = record.Source.DurationMs, AudioSampleRate = 48000,
            Status = "Ready", SourceType = "Generated", CreatedAtUtc = DateTime.UtcNow, VerifiedAtUtc = DateTime.UtcNow, RowVersion = new byte[8],
            MetadataJson = JsonSerializer.Serialize(new { localVoiceJobId = record.Id, sourceFingerprint = record.Source.Fingerprint,
                record.AnchorId, record.AnchorFingerprint, record.RuntimeFingerprint, localVoicePolicy = LocalVoicePolicies.VeoLocalVoiceConsistency,
                nativeAudioAudible = true, convertedAudioAudible = true }) });
        var scene = await db.Scenes.SingleAsync(x => x.SceneId == record.Source.SceneId, token);
        scene.ApprovedRenderMediaAssetId = id;
        scene.Status = "Approved";
        record.OutputAssetId = id;
    }

    internal static async Task<LocalVoiceSource> SourceAsync(VideoFactoryDbContext db, Project project, Guid sceneId, CancellationToken token)
    {
        await RequireApplicableAsync(db, project, token);
        var scene = await db.Scenes.AsNoTracking().Include(x => x.ApprovedGeneration)!.ThenInclude(x => x!.OutputMediaAsset)
            .SingleOrDefaultAsync(x => x.ProjectId == project.ProjectId && x.SceneId == sceneId && x.ScenePlanVersion == project.CurrentScenePlanVersion, token)
            ?? throw new ArgumentException("Cảnh không thuộc kế hoạch hiện hành.");
        var ids = ParseCharacterIds(scene.CharacterIdsJson);
        if (scene.Status != "Approved") throw new ArgumentException("Cần duyệt native clip hiện hành trước khi xử lý giọng.");
        if (string.IsNullOrWhiteSpace(scene.Dialogue) || ids.Length != 1) throw new ArgumentException("Chỉ hỗ trợ cảnh thoại trực diện có một nhân vật.");
        var character = await db.Characters.AsNoTracking().SingleOrDefaultAsync(x => x.CharacterId == ids[0] && x.ProjectId == project.ProjectId, token)
            ?? throw new ArgumentException("Không tìm thấy nhân vật của cảnh.");
        if (character.Status != "Approved" || project.CurrentCharacterVersion is { } version && character.Version != version)
            throw new ArgumentException("Nhân vật phải được duyệt và còn hiệu lực.");
        var generation = scene.ApprovedGeneration;
        var asset = generation?.OutputMediaAsset;
        if (generation is null || generation.SceneId != sceneId || generation.Status != "Approved" || asset is null ||
            asset.ProjectId != project.ProjectId || asset.SceneId != sceneId || asset.AssetType != "SceneVideo" ||
            asset.Status != "Ready" || asset.DeletedAtUtc is not null || generation.RequestedDurationMs is not (4000 or 6000 or 8000))
            throw new ArgumentException("Hãy tải, nghe và duyệt native clip Veo 4/6/8 giây trước.");
        if (!string.Equals(project.WorkspaceRelativePath.Replace('\\', '/').TrimEnd('/'), $"projects/{project.ProjectId:N}", StringComparison.OrdinalIgnoreCase) ||
            Path.IsPathRooted(asset.RelativePath) || asset.RelativePath.Replace('\\', '/').Split('/').Any(part => part is ".." or "."))
            throw new InvalidDataException("Media nguồn không nằm trong workspace của project.");
        var prompt = await db.ScenePrompts.AsNoTracking().Where(x => x.SceneId == sceneId).OrderByDescending(x => x.Version)
            .Select(x => new { x.ScenePromptId, x.Version, x.PromptHash }).FirstOrDefaultAsync(token);
        if (prompt is null || generation.ScenePromptId != prompt.ScenePromptId)
            throw new ArgumentException("Clip không còn khớp prompt hiện hành của cảnh.");
        var references = await db.CharacterReferences.AsNoTracking().Where(x => x.CharacterId == character.CharacterId && x.IsPrimary)
            .OrderBy(x => x.CharacterReferenceId).Select(x => new { x.CharacterReferenceId, x.MediaAssetId, x.ApprovalStatus,
                x.ApprovedAtUtc, x.MediaAsset.Sha256 }).ToListAsync(token);
        var context = LocalVoiceStore.Hash(JsonSerializer.Serialize(new { project.LocalVoicePolicyVersion,
            project.VideoProviderCode, project.VideoModelCode, project.VideoPolicyVersion, project.VideoNativeAudio,
            project.VideoResolution, project.AspectRatio, project.SpeechProductionPolicy,
            scene.ScenePlanVersion, scene.UpdatedAtUtc, scene.Dialogue, scene.ContentDurationMs, scene.HeadTrimMs, scene.TailTrimMs,
            scene.RequiredCapabilitiesJson, prompt, character.CharacterId, character.Version, character.ProfileJson,
            character.WardrobeJson, character.VisualIdentity, character.ForbiddenChangesJson, character.IdentityAnchor, references }));
        return new(project.OrganizationId!.Value, project.RemoteUserId!, project.ProjectId, sceneId, character.CharacterId,
            generation.VideoGenerationId, asset.MediaAssetId, (project.WorkspaceRelativePath + "/" + asset.RelativePath).Replace('\\', '/'),
            asset.Sha256, scene.ContentDurationMs, context);
    }

    internal async Task<bool> IsCurrentAsync(VideoFactoryDbContext db, Project project, LocalVoiceRecord record, CancellationToken token)
    {
        try
        {
            if (record.Source.OwnerId != project.RemoteUserId || record.Source.OrganizationId != project.OrganizationId ||
                project.LocalVoicePolicyVersion != LocalVoicePolicies.VeoLocalVoiceConsistency ||
                (await SourceAsync(db, project, record.Source.SceneId, token)).Fingerprint != record.Source.Fingerprint) return false;
            if (record.NativeException) return true;
            var status = runtime.GetStatus();
            if (status.Fingerprint is not null && status.Fingerprint != record.RuntimeFingerprint) return false;
            if (record.IsAnchor) return true;
            if (record.AnchorId is null) return false;
            var anchor = store.Read(project.ProjectId, record.AnchorId.Value);
            return anchor.IsAnchor && anchor.Status == LocalVoiceStatuses.Approved && anchor.Source.CharacterId == record.Source.CharacterId &&
                anchor.Fingerprint == record.AnchorFingerprint && await IsCurrentAsync(db, project, anchor, token);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or ArgumentException or JsonException) { return false; }
    }

    private async Task<string?> PreviewAsync(LocalVoiceRecord record, CancellationToken token)
    {
        try { await store.RequireHashAsync(record.OutputRelativePath!, record.OutputSha256!, token);
            return "https://media.app.local/" + string.Join('/', record.OutputRelativePath!.Split('/').Select(Uri.EscapeDataString)); }
        catch (Exception e) when (e is IOException or InvalidDataException) { return null; }
    }

    private async Task<string?> SourcePreviewAsync(LocalVoiceSource source, CancellationToken token)
    {
        try { await store.RequireHashAsync(source.RelativePath, source.Sha256, token);
            return "https://media.app.local/" + string.Join('/', source.RelativePath.Split('/').Select(Uri.EscapeDataString)); }
        catch (Exception e) when (e is IOException or InvalidDataException) { return null; }
    }

    private async Task AuthorizeAsync(Guid projectId, Guid org, CancellationToken token)
    {
        var access = await client.AuthorizeLocalVoiceAsync(projectId, token);
        if (access.ProjectId != projectId || access.OrganizationId != org) throw new ArgumentException("Context organization đã thay đổi.");
    }
    private static async Task<Project> RequireProjectAsync(VideoFactoryDbContext db, Guid id, string user, Guid org, CancellationToken token) =>
        await db.Projects.SingleOrDefaultAsync(x => x.ProjectId == id && x.RemoteUserId == user && x.OrganizationId == org && x.DeletedAtUtc == null, token)
        ?? throw new ArgumentException("Không tìm thấy project trong organization hiện hành.");
    private static async Task RequireApplicableAsync(VideoFactoryDbContext db, Project project, CancellationToken token)
    {
        var structure = await db.Scripts.Where(x => x.ProjectId == project.ProjectId && x.Version == project.CurrentScriptVersion)
            .Select(x => x.StructureType).FirstOrDefaultAsync(token);
        if (project.VideoProviderCode != "fal" || project.SpeechProductionPolicy != SpeechProductionPolicies.ProviderNativeVerified || structure != "OpenAiStructuredPlan")
            throw new ArgumentException("Đồng nhất giọng local chỉ áp dụng video dài Fal/Veo dùng Native Audio.");
    }
    private static Guid[] ParseCharacterIds(string? json) => string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<Guid[]>(json) ?? [];
    private static void RequireConfirmed(bool confirmed) { if (!confirmed) throw new ArgumentException("Cần xác nhận thao tác và nghe kiểm tra trước khi duyệt."); }
    private static string? SafeReason(string? reason) => string.IsNullOrWhiteSpace(reason) ? null : reason.Trim().Length <= 500
        ? reason.Trim() : throw new ArgumentException("Lý do tối đa 500 ký tự.");
    public void Dispose() { _active?.Cancel(); }
}
