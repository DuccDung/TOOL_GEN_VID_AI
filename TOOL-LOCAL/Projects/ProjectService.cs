using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using TOOL_LOCAL.Data;
using TOOL_LOCAL.Data.Models;
using TOOL_LOCAL.Jobs;
using TOOL_LOCAL.Storage;
using TOOL_SHARED.Contracts.Authentication;
using TOOL_SHARED.Contracts.Generation;

namespace TOOL_LOCAL.Projects;

public sealed class ProjectService(
    IDbContextFactory<VideoFactoryDbContext> dbContextFactory,
    ProjectWorkspaceService workspaceService) : IProjectService
{
    public async Task<IReadOnlyList<ProjectSummary>> ListAsync(
        string remoteUserId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.Projects
            .AsNoTracking()
            .Where(x => x.RemoteUserId == remoteUserId && x.DeletedAtUtc == null)
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Select(x => new ProjectSummary(
                x.ProjectId,
                x.OrganizationId,
                x.Name,
                x.Topic,
                x.Platform,
                x.AspectRatio,
                x.TargetDurationSeconds,
                x.Status,
                x.ActualCost,
                x.BudgetLimit,
                x.UpdatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    public async Task<ProjectDashboard?> GetDashboardAsync(
        Guid projectId,
        string remoteUserId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var project = await dbContext.Projects
            .AsNoTracking()
            .Where(x =>
                x.ProjectId == projectId &&
                x.RemoteUserId == remoteUserId &&
                x.DeletedAtUtc == null)
            .Select(x => new
            {
                x.ProjectId,
                x.OrganizationId,
                x.Name,
                x.Topic,
                x.Platform,
                x.AspectRatio,
                x.LanguageCode,
                x.VoiceCode,
                x.VoiceSpeakingRate,
                x.TargetDurationSeconds,
                x.Status,
                x.ActualCost,
                x.BudgetLimit,
                x.WorkspaceRelativePath,
                x.VideoProviderCode,
                x.VideoModelCode,
                x.CurrentScriptVersion,
                x.CurrentStyleVersion,
                x.CurrentScenePlanVersion,
                x.CurrentCharacterVersion,
                x.CreatedAtUtc,
                x.UpdatedAtUtc,
                x.LastErrorMessage
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (project is null)
        {
            return null;
        }
        var currentScriptVersion = project.CurrentScriptVersion;
        var currentScript = await dbContext.Scripts
            .AsNoTracking()
            .Where(x =>
                x.ProjectId == projectId &&
                (x.Status == "Approved" || x.Version == currentScriptVersion))
            .OrderByDescending(x => x.Version == currentScriptVersion)
            .ThenByDescending(x => x.Version)
            .Select(x => new
            {
                x.Version,
                x.StructureType,
                x.Title,
                x.FullText,
                ConceptTitle = x.Concept == null ? null : x.Concept.Title,
                ConceptHook = x.Concept == null ? null : x.Concept.SelectedHook,
                ConceptAngle = x.Concept == null ? null : x.Concept.Angle,
                ConceptAudience = x.Concept == null ? null : x.Concept.Audience,
                ConceptCallToAction = x.Concept == null ? null : x.Concept.CallToAction
            })
            .FirstOrDefaultAsync(cancellationToken);
        var workflowStructureType = currentScript?.StructureType;
        var currentStyle = project.CurrentStyleVersion is null
            ? null
            : await dbContext.StyleProfiles
                .AsNoTracking()
                .Where(x => x.ProjectId == projectId && x.Version == project.CurrentStyleVersion.Value)
                .Select(x => new { x.VisualStyleJson, x.NegativeRulesJson })
                .SingleOrDefaultAsync(cancellationToken);

        var progress = await dbContext.VwProjectProgresses
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .Select(x => new
            {
                x.TotalScenes,
                x.ApprovedScenes,
                x.FailedScenes,
                x.PendingJobs,
                x.RunningJobs,
                x.FailedJobs
            })
            .SingleOrDefaultAsync(cancellationToken);

        var jobs = await dbContext.Jobs
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .OrderBy(x => x.CreatedAtUtc)
            .Select(x => new DashboardJob(x.JobType, x.Status, x.ProgressPercent))
            .ToListAsync(cancellationToken);

        var render = await dbContext.RenderJobs
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .OrderByDescending(x => x.Version)
            .Select(x => new { x.Status, x.ProgressPercent })
            .FirstOrDefaultAsync(cancellationToken);

        var finalVideoCandidates = await dbContext.FinalVideos
            .AsNoTracking()
            .Where(x =>
                x.ProjectId == projectId &&
                x.RenderJob.Status == "Completed" &&
                x.MediaAsset.AssetType == "FinalVideo" &&
                x.MediaAsset.Status == "Ready" &&
                x.MediaAsset.DeletedAtUtc == null &&
                x.Status != "Rejected" &&
                x.Status != "Invalid")
            .OrderByDescending(x => x.Version)
            .Select(x => new PreviewAsset(
                x.ExportedPath,
                x.MediaAsset.RelativePath,
                x.MediaAsset.DurationMs,
                x.MediaAsset.MimeType))
            .ToListAsync(cancellationToken);
        var finalVideoPreview = finalVideoCandidates
            .Select(candidate => CreatePreview(project.WorkspaceRelativePath, candidate))
            .FirstOrDefault(preview => preview is not null);

        var characterRows = project.CurrentCharacterVersion.HasValue
            ? await dbContext.Characters
                .AsNoTracking()
                .Where(x =>
                    x.ProjectId == projectId &&
                    x.Version == project.CurrentCharacterVersion.Value &&
                    x.Status != "Superseded")
                .OrderBy(x => x.Name)
                .Select(x => new
                {
                    x.CharacterId,
                    x.CharacterKey,
                    x.Version,
                    x.Name,
                    x.Role,
                    x.VisualIdentity,
                    x.ProfileJson,
                    x.WardrobeJson,
                    x.ForbiddenChangesJson,
                    x.Status,
                    PrimaryReference = x.CharacterReferences
                        .Where(reference =>
                            reference.IsPrimary &&
                            reference.ApprovalStatus == "Approved" &&
                            reference.MediaAsset.Status == "Ready" &&
                            reference.MediaAsset.DeletedAtUtc == null)
                        .OrderByDescending(reference => reference.CreatedAtUtc)
                        .Select(reference => new
                        {
                            reference.CharacterReferenceId,
                            reference.ReferenceType,
                            reference.IsPrimary,
                            reference.ApprovalStatus,
                            reference.MediaAsset.RelativePath,
                            reference.MediaAsset.MimeType
                        })
                        .FirstOrDefault()
                })
                .ToListAsync(cancellationToken)
            : [];

        var sceneRows = project.CurrentScenePlanVersion.HasValue
            ? await dbContext.Scenes
                .AsNoTracking()
                .Where(x =>
                    x.ProjectId == projectId &&
                    x.ScenePlanVersion == project.CurrentScenePlanVersion.Value)
                .OrderBy(x => x.SequenceNumber)
                .Select(x => new
                {
                    x.SceneId,
                    x.SequenceNumber,
                    x.TimelineStartMs,
                    x.TimelineEndMs,
                    DurationMs = x.ContentDurationMs,
                    x.GenerationDurationMs,
                    x.StoryPurpose,
                    x.Narration,
                    x.Dialogue,
                    x.VisualDescription,
                    x.Status,
                    x.ApprovedGenerationId,
                    x.LastErrorCode,
                    x.LastErrorMessage,
                    x.CharacterIdsJson,
                    x.RequiredCapabilitiesJson,
                    Prompt = x.ScenePrompts
                        .OrderByDescending(prompt => prompt.Version)
                        .Select(prompt => prompt.FinalPrompt)
                        .FirstOrDefault(),
                    NegativePrompt = x.ScenePrompts
                        .OrderByDescending(prompt => prompt.Version)
                        .Select(prompt => prompt.NegativePrompt)
                        .FirstOrDefault(),
                    HasActiveProviderRequest = x.ProviderRequests.Any(request =>
                        request.Status != "Completed" &&
                        request.Status != "Failed" &&
                        request.Status != "Cancelled" &&
                        request.Status != "Expired")
                })
                .ToListAsync(cancellationToken)
            : [];
        var sceneIds = sceneRows.Select(x => x.SceneId).ToArray();
        var sceneAssets = sceneIds.Length == 0
            ? []
            : await dbContext.MediaAssets
                .AsNoTracking()
                .Where(x =>
                    x.SceneId.HasValue &&
                    sceneIds.Contains(x.SceneId.Value) &&
                    x.AssetType == "SceneVideo" &&
                    x.Status == "Ready" &&
                    x.DeletedAtUtc == null)
                .OrderByDescending(x => x.CreatedAtUtc)
                .Select(x => new SceneAsset(
                    x.SceneId!.Value,
                    x.RelativePath,
                    x.DurationMs,
                    x.MimeType,
                    x.AssetType,
                    x.MetadataJson))
                .ToListAsync(cancellationToken);
        var latestAssetByScene = sceneAssets
            .GroupBy(x => x.SceneId)
            .ToDictionary(x => x.Key, x => x.First());
        var characterIdsByScene = sceneRows.ToDictionary(
            scene => scene.SceneId,
            scene => ParseGuidList(scene.CharacterIdsJson));
        var characterSummaries = characterRows
            .Select(character =>
            {
                var referencePreview = character.PrimaryReference is null
                    ? null
                    : CreatePreview(
                        project.WorkspaceRelativePath,
                        new PreviewAsset(
                            null,
                            character.PrimaryReference.RelativePath,
                            null,
                            character.PrimaryReference.MimeType));
                var primaryReference = character.PrimaryReference is null
                    ? null
                    : new CharacterReferenceSummary(
                        character.PrimaryReference.CharacterReferenceId,
                        character.PrimaryReference.ReferenceType,
                        character.PrimaryReference.IsPrimary,
                        character.PrimaryReference.ApprovalStatus,
                        referencePreview?.Url,
                        character.PrimaryReference.MimeType);
                var sceneCount = characterIdsByScene.Values.LongCount(ids => ids.Contains(character.CharacterId));
                var canApprove = character.Status == "Draft" && primaryReference is not null;
                var setupMessage = character.Status == "Approved" && primaryReference is not null
                    ? null
                    : primaryReference is null
                        ? "Hãy chọn một ảnh tham chiếu chính diện trước khi khóa nhân vật."
                        : "Hãy kiểm tra hồ sơ và khóa nhân vật trước khi tạo clip.";
                return new CharacterDashboardSummary(
                    character.CharacterId,
                    character.CharacterKey,
                    character.Version,
                    character.Name,
                    character.Role,
                    character.VisualIdentity ?? string.Empty,
                    FormatWardrobe(character.WardrobeJson),
                    ReadStringArrayProperty(character.ProfileJson, "immutableTraits"),
                    ParseStringList(character.ForbiddenChangesJson),
                    character.Status,
                    sceneCount,
                    primaryReference,
                    character.Status == "Draft",
                    canApprove,
                    setupMessage);
            })
            .ToArray();
        var characterById = characterSummaries.ToDictionary(character => character.CharacterId);
        var sceneSummaries = sceneRows
            .Select(scene =>
            {
                latestAssetByScene.TryGetValue(scene.SceneId, out var asset);
                var sceneCharacters = characterIdsByScene[scene.SceneId]
                    .Where(characterById.ContainsKey)
                    .Select(characterId => characterById[characterId])
                    .Select(character => new SceneCharacterSummary(
                        character.CharacterId,
                        character.Name,
                        character.Status,
                        character.PrimaryReference?.PreviewUrl))
                    .ToArray();
                var characterReady = characterIdsByScene[scene.SceneId].Count == sceneCharacters.Length &&
                                     sceneCharacters.All(character =>
                                         character.Status == "Approved" &&
                                         !string.IsNullOrWhiteSpace(character.ReferencePreviewUrl));
                var characterSetupMessage = characterReady
                    ? null
                    : "Cảnh này cần khóa nhân vật và chọn ảnh tham chiếu trước khi tạo video.";
                var spokenText = !string.IsNullOrWhiteSpace(scene.Dialogue) ? scene.Dialogue : scene.Narration;
                var speechMode = !string.IsNullOrWhiteSpace(scene.Dialogue)
                    ? KlingSpeechModes.OnCameraDialogue
                    : !string.IsNullOrWhiteSpace(scene.Narration)
                        ? KlingSpeechModes.NativeVoiceOver
                        : KlingSpeechModes.None;
                var nativeAudioPresent = ReadBooleanProperty(asset?.MetadataJson, "nativeAudioPresent");
                var nativeAudioAudible = ReadBooleanProperty(asset?.MetadataJson, "nativeAudioAudible");
                var requiresAudioReview = scene.Status == "AudioReviewRequired";
                var canApproveNativeAudio = requiresAudioReview &&
                                                asset is not null &&
                                                nativeAudioAudible;
                var speakerCharacterName = speechMode == KlingSpeechModes.OnCameraDialogue
                    ? sceneCharacters.SingleOrDefault()?.Name
                    : null;
                return new SceneDashboardSummary(
                    scene.SceneId,
                    scene.SequenceNumber,
                    scene.TimelineStartMs,
                    scene.TimelineEndMs,
                    scene.DurationMs,
                    scene.GenerationDurationMs,
                    scene.StoryPurpose,
                    spokenText,
                    scene.VisualDescription,
                    scene.Prompt ?? string.Empty,
                    scene.Status,
                    scene.ApprovedGenerationId is null &&
                        !scene.HasActiveProviderRequest &&
                        scene.Status != "AudioReviewRequired",
                    scene.ApprovedGenerationId is null &&
                        !scene.HasActiveProviderRequest &&
                        scene.Status != "AudioReviewRequired" &&
                        !string.IsNullOrWhiteSpace(scene.Prompt) && characterReady,
                    sceneCharacters,
                    characterSetupMessage,
                    asset is null
                        ? null
                        : CreatePreview(
                            project.WorkspaceRelativePath,
                            new PreviewAsset(null, asset.RelativePath, asset.DurationMs, asset.MimeType)),
                    string.IsNullOrWhiteSpace(scene.LastErrorMessage)
                        ? null
                        : IsMediaToolError(scene.LastErrorCode) ||
                          IsNativeAudioError(scene.LastErrorCode) ||
                          IsVideoProviderRetryError(scene.LastErrorCode)
                            ? scene.LastErrorMessage
                            : "Không thể hoàn tất clip cho cảnh này. Hãy kiểm tra prompt và thử lại.",
                    scene.LastErrorCode,
                    false,
                    speechMode,
                    nativeAudioPresent,
                    nativeAudioAudible,
                    requiresAudioReview,
                    canApproveNativeAudio,
                    speakerCharacterName,
                    ReadStringProperty(scene.RequiredCapabilitiesJson, "voiceStyle"),
                    ReadStringProperty(scene.RequiredCapabilitiesJson, "ambientAudio"),
                    ReadStringProperty(scene.RequiredCapabilitiesJson, "soundEffects"));
            })
            .ToArray();

        var totalScenes = progress?.TotalScenes ?? 0;
        var approvedScenes = progress?.ApprovedScenes ?? 0;
        var failedScenes = progress?.FailedScenes ?? 0;
        var pendingJobs = progress?.PendingJobs ?? 0;
        var runningJobs = progress?.RunningJobs ?? 0;
        var failedJobs = progress?.FailedJobs ?? 0;
        var hasConcept = await dbContext.Concepts.AsNoTracking().AnyAsync(
            x => x.ProjectId == projectId && x.Status == "Approved",
            cancellationToken);
        var hasScript = await dbContext.Scripts.AsNoTracking().AnyAsync(
            x => x.ProjectId == projectId && x.Status == "Approved",
            cancellationToken);
        var promptCount = project.CurrentScenePlanVersion.HasValue
            ? await dbContext.ScenePrompts.AsNoTracking().CountAsync(
                x => x.Scene.ProjectId == projectId &&
                     x.Scene.ScenePlanVersion == project.CurrentScenePlanVersion &&
                     (x.Status == "Ready" || x.Status == "Approved"),
                cancellationToken)
            : 0;
        var hasVideoActivity = await dbContext.VideoGenerations.AsNoTracking().AnyAsync(
            x => x.Scene.ProjectId == projectId,
            cancellationToken);
        var pipeline = CreatePipeline(
            jobs,
            totalScenes,
            approvedScenes,
            hasConcept,
            hasScript,
            promptCount,
            hasVideoActivity,
            render is not null);
        var overallProgress = pipeline.Count == 0
            ? 0
            : Math.Round(pipeline.Average(x => x.ProgressPercent), 2);
        var renderProgress = render?.ProgressPercent ?? pipeline.Last().ProgressPercent;
        var audioStrategy = sceneRows.Count > 0 &&
                            sceneRows.All(scene =>
                                ReadBooleanProperty(scene.RequiredCapabilitiesJson, "muteOutputAudio"))
            ? "SilentOutput"
            : "ProviderNative";
        var requiresKlingVietnamese = KlingLongFormVietnameseValidator.RequiresVietnamese(
            project.VideoProviderCode,
            workflowStructureType);
        var requiresVietnameseContentRegeneration = requiresKlingVietnamese &&
            (sceneRows.Any(scene => !string.Equals(
                 ReadStringProperty(scene.RequiredCapabilitiesJson, "effectiveGenerationLanguageCode"),
                 KlingLongFormVietnameseValidator.EffectiveLanguageCode,
                 StringComparison.OrdinalIgnoreCase)) ||
             KlingLongFormVietnameseValidator.HasNonVietnameseContent(
                new string?[]
                {
                    currentScript?.Title,
                    currentScript?.FullText,
                    currentScript?.ConceptTitle,
                    currentScript?.ConceptHook,
                    currentScript?.ConceptAngle,
                    currentScript?.ConceptAudience,
                    currentScript?.ConceptCallToAction,
                    ReadStringProperty(currentStyle?.VisualStyleJson, "description"),
                    ReadStringProperty(currentStyle?.NegativeRulesJson, "prompt")
                }.Concat(sceneRows.SelectMany(scene => new string?[]
                {
                    scene.StoryPurpose,
                    scene.Narration,
                    scene.Dialogue,
                    scene.VisualDescription,
                    scene.Prompt,
                    scene.NegativePrompt,
                    ReadStringProperty(scene.RequiredCapabilitiesJson, "voiceStyle"),
                    ReadStringProperty(scene.RequiredCapabilitiesJson, "ambientAudio"),
                    ReadStringProperty(scene.RequiredCapabilitiesJson, "soundEffects")
                }).Concat(characterRows.SelectMany(character => new string?[]
                {
                    character.Role,
                    character.VisualIdentity,
                    ReadStringProperty(character.ProfileJson, "gender"),
                    ReadStringProperty(character.ProfileJson, "face"),
                    ReadStringProperty(character.ProfileJson, "hair"),
                    ReadStringProperty(character.ProfileJson, "skin"),
                    ReadStringProperty(character.ProfileJson, "body"),
                    string.Join("; ", ReadStringArrayProperty(character.ProfileJson, "immutableTraits")),
                    FormatWardrobe(character.WardrobeJson),
                    string.Join("; ", ParseStringList(character.ForbiddenChangesJson))
                })))));

        var summary = new ProjectSummary(
            project.ProjectId,
            project.OrganizationId,
            project.Name,
            project.Topic,
            project.Platform,
            project.AspectRatio,
            project.TargetDurationSeconds,
            project.Status,
            project.ActualCost,
            project.BudgetLimit,
            project.UpdatedAtUtc);
        var content = currentScript is null || string.IsNullOrWhiteSpace(currentScript.FullText)
            ? null
            : new ProjectContentSummary(
                currentScript.Version,
                !string.IsNullOrWhiteSpace(currentScript.Title)
                    ? currentScript.Title
                    : !string.IsNullOrWhiteSpace(currentScript.ConceptTitle)
                        ? currentScript.ConceptTitle
                        : project.Topic,
                currentScript.FullText,
                currentScript.ConceptHook,
                currentScript.ConceptAngle,
                currentScript.ConceptAudience,
                currentScript.ConceptCallToAction);

        return new ProjectDashboard(
            summary,
            project.LanguageCode,
            project.CreatedAtUtc,
            totalScenes,
            approvedScenes,
            failedScenes,
            pendingJobs,
            runningJobs,
            failedJobs,
            overallProgress,
            pipeline,
            new RenderProgressSummary(
                render?.Status ?? "Pending",
                renderProgress,
                approvedScenes,
                totalScenes,
                null),
            characterSummaries,
            sceneSummaries,
            finalVideoPreview,
            string.IsNullOrWhiteSpace(project.LastErrorMessage)
                ? null
                : "Dự án gặp lỗi ở bước xử lý gần nhất.",
            project.VoiceCode,
            project.VoiceSpeakingRate,
            audioStrategy,
            project.VideoProviderCode,
            project.VideoModelCode,
            workflowStructureType,
            requiresKlingVietnamese
                ? KlingLongFormVietnameseValidator.EffectiveLanguageCode
                : project.LanguageCode,
            requiresVietnameseContentRegeneration,
            content);
    }

    public async Task UpdateSceneAsync(
        Guid projectId,
        string remoteUserId,
        UpdateSceneCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.SceneId == Guid.Empty)
        {
            throw new ArgumentException("Cảnh cần cập nhật không hợp lệ.", nameof(command));
        }

        var narration = command.Narration?.Trim();
        var speechMode = command.SpeechMode?.Trim();
        var voiceStyle = NormalizeOptionalSceneAudioText(command.VoiceStyle, 1000, "Phong cách giọng");
        var ambientAudio = NormalizeOptionalSceneAudioText(command.AmbientAudio, 1000, "Âm thanh môi trường");
        var soundEffects = NormalizeOptionalSceneAudioText(command.SoundEffects, 1000, "Hiệu ứng âm thanh");
        var visualDescription = command.VisualDescription?.Trim() ?? string.Empty;
        var promptText = command.Prompt?.Trim() ?? string.Empty;
        if (narration?.Length > 4000)
        {
            throw new ArgumentException("Lời thoại Native Audio không được vượt quá 4.000 ký tự.", nameof(command));
        }
        if (speechMode is not (KlingSpeechModes.None or
            KlingSpeechModes.OnCameraDialogue or
            KlingSpeechModes.NativeVoiceOver))
        {
            throw new ArgumentException("Kiểu lời Native Audio không hợp lệ.", nameof(command));
        }
        if (speechMode == KlingSpeechModes.None && !string.IsNullOrWhiteSpace(narration))
        {
            throw new ArgumentException("Cảnh không lời không được chứa nội dung lời thoại.", nameof(command));
        }
        if (speechMode != KlingSpeechModes.None && string.IsNullOrWhiteSpace(narration))
        {
            throw new ArgumentException("Hãy nhập nội dung nhân vật hoặc giọng dẫn cần nói.", nameof(command));
        }
        if (visualDescription.Length is < 1 or > 12000)
        {
            throw new ArgumentException("Mô tả hình ảnh phải có từ 1 đến 12.000 ký tự.", nameof(command));
        }
        if (promptText.Length is < 1 or > 12000)
        {
            throw new ArgumentException("Prompt video phải có từ 1 đến 12.000 ký tự.", nameof(command));
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var project = await dbContext.Projects
            .SingleOrDefaultAsync(
                x => x.ProjectId == projectId &&
                     x.RemoteUserId == remoteUserId &&
                     x.DeletedAtUtc == null,
                cancellationToken)
            ?? throw new ArgumentException("Không tìm thấy dự án hoặc bạn không có quyền cập nhật.");
        if (!project.CurrentScenePlanVersion.HasValue)
        {
            throw new ArgumentException("Dự án chưa có kế hoạch cảnh để cập nhật.");
        }

        var scene = await dbContext.Scenes
            .Include(x => x.ScenePrompts)
            .Include(x => x.ProviderRequests)
            .SingleOrDefaultAsync(
                x => x.SceneId == command.SceneId &&
                     x.ProjectId == projectId &&
                     x.ScenePlanVersion == project.CurrentScenePlanVersion.Value,
                cancellationToken)
            ?? throw new ArgumentException("Không tìm thấy cảnh trong kế hoạch hiện hành.");
        var hasActiveRequest = scene.ProviderRequests.Any(
            x => x.Status is not ("Completed" or "Failed" or "Cancelled" or "Expired"));
        var hasCompletedRequest = scene.ProviderRequests.Any(x => x.Status == "Completed");
        if (scene.ApprovedGenerationId.HasValue ||
            hasActiveRequest ||
            (hasCompletedRequest && scene.Status != "NativeAudioInvalid"))
        {
            throw new ArgumentException("Cảnh đã được gửi sang provider video nên không thể sửa prompt hiện hành.");
        }
        var characterCount = ParseGuidList(scene.CharacterIdsJson).Count;
        if (speechMode == KlingSpeechModes.OnCameraDialogue && characterCount != 1)
        {
            throw new ArgumentException("Thoại trực tiếp cần đúng một nhân vật trong cảnh.", nameof(command));
        }
        var previousPrompt = scene.ScenePrompts
            .OrderByDescending(x => x.Version)
            .FirstOrDefault()
            ?? throw new ArgumentException("Cảnh chưa có prompt để cập nhật.");
        var structureType = await dbContext.Scripts
            .AsNoTracking()
            .Where(x => x.ScriptId == scene.ScriptId && x.ProjectId == projectId)
            .Select(x => x.StructureType)
            .SingleOrDefaultAsync(cancellationToken);
        if (KlingLongFormSpeechIntentValidator.Applies(project.VideoProviderCode, structureType))
        {
            var violation = KlingLongFormSpeechIntentValidator.FindViolation(
                speechMode,
                narration,
                speechMode == KlingSpeechModes.OnCameraDialogue ? "scene-character" : null,
                characterCount);
            if (violation is not null)
            {
                throw new ArgumentException(violation, nameof(command));
            }
        }
        if (KlingLongFormVietnameseValidator.RequiresVietnamese(project.VideoProviderCode, structureType))
        {
            KlingLongFormVietnameseValidator.RequireVietnamese(
                [
                    narration,
                    voiceStyle,
                    ambientAudio,
                    soundEffects,
                    visualDescription,
                    promptText,
                    previousPrompt.NegativePrompt
                ],
                "Nội dung cảnh của video dài dùng provider Native Audio phải bằng tiếng Việt. Hãy nhập tiếng Việt hoặc sinh lại nội dung tiếng Việt.");
        }
        previousPrompt.Status = "Superseded";
        var now = DateTime.UtcNow;
        // The scene is already tracked as an existing aggregate. Adding a prompt only
        // through its navigation can make EF infer Modified for the dependent because
        // ScenePromptId is database-generated but already has a GUID. Mark it Added
        // explicitly so SaveChanges issues INSERT rather than UPDATE.
        dbContext.ScenePrompts.Add(new ScenePrompt
        {
            ScenePromptId = Guid.NewGuid(),
            SceneId = scene.SceneId,
            Version = previousPrompt.Version + 1,
            PromptTemplateName = "manual-storyboard-edit",
            PromptTemplateVersion = "2",
            CanonicalInputJson = JsonSerializer.Serialize(new
            {
                speechMode,
                spokenText = narration,
                voiceStyle,
                ambientAudio,
                soundEffects,
                visualDescription,
                prompt = promptText
            }),
            FinalPrompt = promptText,
            NegativePrompt = previousPrompt.NegativePrompt,
            PromptHash = Sha256Hex(
                promptText + "\n" + previousPrompt.NegativePrompt + "\n" + speechMode + "\n" + narration +
                "\n" + voiceStyle + "\n" + ambientAudio + "\n" + soundEffects),
            Status = "Approved",
            CreatedAtUtc = now,
            ApprovedAtUtc = now
        });
        scene.Dialogue = speechMode == KlingSpeechModes.OnCameraDialogue ? narration : null;
        scene.Narration = speechMode == KlingSpeechModes.NativeVoiceOver ? narration : null;
        scene.RequiredCapabilitiesJson = MergeNativeAudioCapabilities(
            scene.RequiredCapabilitiesJson,
            speechMode,
            voiceStyle,
            ambientAudio,
            soundEffects);
        scene.VisualDescription = visualDescription;
        scene.Status = scene.Status == "NativeAudioInvalid" ? "NativeAudioInvalid" : "PromptReady";
        scene.LastErrorCode = null;
        scene.LastErrorMessage = null;
        scene.UpdatedAtUtc = now;
        project.Status = "ScenePlanning";
        project.LastErrorCode = null;
        project.LastErrorMessage = null;
        project.UpdatedAtUtc = now;
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ArgumentException(
                "Dữ liệu dự án vừa thay đổi. Nội dung bạn sửa vẫn được giữ; hãy bấm Lưu cảnh lại.",
                nameof(command),
                exception);
        }
    }

    public async Task ApproveSceneNativeAudioAsync(
        Guid projectId,
        string remoteUserId,
        Guid sceneId,
        bool playbackConfirmed,
        CancellationToken cancellationToken = default)
    {
        if (sceneId == Guid.Empty)
        {
            throw new ArgumentException("Cảnh cần duyệt không hợp lệ.", nameof(sceneId));
        }

        if (!playbackConfirmed)
        {
            throw new ArgumentException("Hãy phát và nghe clip trước khi duyệt Native Audio.", nameof(playbackConfirmed));
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var project = await dbContext.Projects.SingleOrDefaultAsync(
            x => x.ProjectId == projectId && x.RemoteUserId == remoteUserId && x.DeletedAtUtc == null,
            cancellationToken)
            ?? throw new ArgumentException("Không tìm thấy dự án hoặc bạn không có quyền duyệt cảnh.");
        if (!project.CurrentScenePlanVersion.HasValue)
        {
            throw new ArgumentException("Dự án chưa có kế hoạch cảnh hiện hành.");
        }

        var scene = await dbContext.Scenes.SingleOrDefaultAsync(
            x => x.SceneId == sceneId &&
                 x.ProjectId == projectId &&
                 x.ScenePlanVersion == project.CurrentScenePlanVersion.Value,
            cancellationToken)
            ?? throw new ArgumentException("Không tìm thấy cảnh cần duyệt.");
        if (scene.Status != "AudioReviewRequired" || scene.ApprovedGenerationId.HasValue)
        {
            throw new ArgumentException("Cảnh chưa ở trạng thái chờ duyệt Native Audio.");
        }

        var generation = await dbContext.VideoGenerations
            .Include(x => x.OutputMediaAsset)
            .Where(x => x.SceneId == sceneId &&
                        x.OutputMediaAssetId != null &&
                        x.OutputMediaAsset!.AssetType == "SceneVideo" &&
                        x.OutputMediaAsset.Status == "Ready" &&
                        x.OutputMediaAsset.DeletedAtUtc == null)
            .OrderByDescending(x => x.AttemptNumber)
            .ThenByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new ArgumentException("Cảnh chưa có clip video hợp lệ để duyệt.");
        var nativeAudioAudible = ReadBooleanProperty(
            generation.OutputMediaAsset!.MetadataJson,
            "nativeAudioAudible");
        if (!nativeAudioAudible)
        {
            throw new ArgumentException("Clip chưa có Native Audio nghe được nên không thể duyệt.");
        }

        var now = DateTime.UtcNow;
        generation.Status = "Approved";
        generation.CompletedAtUtc ??= now;
        scene.ApprovedGenerationId = generation.VideoGenerationId;
        scene.Status = "Approved";
        scene.LastErrorCode = null;
        scene.LastErrorMessage = null;
        scene.UpdatedAtUtc = now;
        var allOtherScenesApproved = await dbContext.Scenes
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId &&
                        x.ScenePlanVersion == project.CurrentScenePlanVersion.Value &&
                        x.SceneId != sceneId)
            .AllAsync(x => x.ApprovedGenerationId != null, cancellationToken);
        project.Status = allOtherScenesApproved ? "ReadyToRender" : "ScenePlanning";
        project.LastErrorCode = null;
        project.LastErrorMessage = null;
        project.UpdatedAtUtc = now;
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task UpdateCharacterAsync(
        Guid projectId,
        string remoteUserId,
        UpdateCharacterCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.CharacterId == Guid.Empty)
        {
            throw new ArgumentException("Nhân vật cần cập nhật không hợp lệ.", nameof(command));
        }

        var name = command.Name?.Trim() ?? string.Empty;
        var role = command.Role?.Trim();
        var visualIdentity = command.VisualIdentity?.Trim() ?? string.Empty;
        var wardrobe = command.Wardrobe?.Trim() ?? string.Empty;
        var immutableTraits = NormalizeCharacterRules(command.ImmutableTraits, "Đặc điểm cố định");
        var forbiddenChanges = NormalizeCharacterRules(command.ForbiddenChanges, "Điều cấm thay đổi");
        if (name.Length is < 1 or > 200 || visualIdentity.Length is < 1 or > 4000 || wardrobe.Length is < 1 or > 4000)
        {
            throw new ArgumentException("Tên, nhận diện hình ảnh hoặc trang phục nhân vật không hợp lệ.", nameof(command));
        }
        if (role?.Length > 200)
        {
            throw new ArgumentException("Vai trò nhân vật không được vượt quá 200 ký tự.", nameof(command));
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var character = await dbContext.Characters
            .Include(x => x.Project)
            .SingleOrDefaultAsync(
                x => x.CharacterId == command.CharacterId &&
                     x.ProjectId == projectId &&
                     x.Project.RemoteUserId == remoteUserId &&
                     x.Project.DeletedAtUtc == null,
                cancellationToken)
            ?? throw new ArgumentException("Không tìm thấy nhân vật trong dự án hiện tại.");
        if (character.Status != "Draft")
        {
            throw new ArgumentException("Nhân vật đã khóa. Hãy sinh phiên bản content mới nếu cần thay đổi nhận diện.");
        }
        var structureType = character.Project.CurrentScriptVersion is null
            ? null
            : await dbContext.Scripts
                .AsNoTracking()
                .Where(x => x.ProjectId == projectId && x.Version == character.Project.CurrentScriptVersion.Value)
                .Select(x => x.StructureType)
                .SingleOrDefaultAsync(cancellationToken);
        if (KlingLongFormVietnameseValidator.RequiresVietnamese(character.Project.VideoProviderCode, structureType))
        {
            KlingLongFormVietnameseValidator.RequireVietnamese(
                [role, visualIdentity, wardrobe, .. immutableTraits, .. forbiddenChanges],
                "Hồ sơ nhân vật của video dài dùng provider Native Audio phải bằng tiếng Việt. Hãy nhập tiếng Việt hoặc sinh lại nội dung tiếng Việt.");
        }

        var profile = JsonNode.Parse(character.ProfileJson) as JsonObject ?? new JsonObject();
        profile["name"] = name;
        profile["role"] = role ?? string.Empty;
        profile["visualIdentity"] = visualIdentity;
        profile["clothing"] = wardrobe;
        profile["immutableTraits"] = new JsonArray(immutableTraits.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray());
        profile["forbiddenChanges"] = new JsonArray(forbiddenChanges.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray());
        character.Name = name;
        character.Role = string.IsNullOrWhiteSpace(role) ? null : role;
        character.VisualIdentity = visualIdentity;
        character.ProfileJson = profile.ToJsonString();
        character.WardrobeJson = JsonSerializer.Serialize(new { clothing = wardrobe, accessories = string.Empty });
        character.ForbiddenChangesJson = JsonSerializer.Serialize(forbiddenChanges);
        character.Project.UpdatedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ImportCharacterReferenceAsync(
        Guid projectId,
        string remoteUserId,
        Guid characterId,
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        if (characterId == Guid.Empty || string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new ArgumentException("Ảnh tham chiếu nhân vật không hợp lệ.");
        }

        var fullSourcePath = Path.GetFullPath(sourcePath);
        var fileInfo = new FileInfo(fullSourcePath);
        if (!fileInfo.Exists || fileInfo.Length is <= 0 or > 10 * 1024 * 1024)
        {
            throw new ArgumentException("Ảnh tham chiếu phải có dung lượng từ 1 byte đến 10 MB.");
        }

        var imageInfo = await ValidateCharacterImageAsync(fullSourcePath, cancellationToken);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var character = await dbContext.Characters
            .Include(x => x.CharacterReferences)
            .Include(x => x.Project)
            .SingleOrDefaultAsync(
                x => x.CharacterId == characterId &&
                     x.ProjectId == projectId &&
                     x.Project.RemoteUserId == remoteUserId &&
                     x.Project.DeletedAtUtc == null,
                cancellationToken)
            ?? throw new ArgumentException("Không tìm thấy nhân vật trong dự án hiện tại.");
        if (character.Status != "Draft")
        {
            throw new ArgumentException("Nhân vật đã khóa nên không thể thay ảnh tham chiếu.");
        }

        var fileName = $"character-{character.CharacterKey}-{Guid.NewGuid():N}{imageInfo.Extension}";
        var assetRelativePath = Path.Combine("characters", fileName).Replace(Path.DirectorySeparatorChar, '/');
        var workspaceRelativePath = Path.Combine(character.Project.WorkspaceRelativePath, "characters", fileName);
        var destinationPath = workspaceService.Resolve(workspaceRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        string sha256;
        await using (var source = new FileStream(fullSourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            sha256 = Convert.ToHexString(await SHA256.HashDataAsync(source, cancellationToken)).ToLowerInvariant();
            source.Position = 0;
            await using var destination = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await source.CopyToAsync(destination, cancellationToken);
        }

        try
        {
            foreach (var existing in character.CharacterReferences.Where(reference => reference.IsPrimary))
            {
                existing.IsPrimary = false;
            }

            var now = DateTime.UtcNow;
            var asset = new MediaAsset
            {
                MediaAssetId = Guid.NewGuid(),
                ProjectId = projectId,
                AssetType = "CharacterReference",
                DisplayName = $"Ảnh tham chiếu {character.Name}",
                RelativePath = assetRelativePath,
                MimeType = imageInfo.MimeType,
                SizeBytes = fileInfo.Length,
                Sha256 = sha256,
                Width = imageInfo.Width,
                Height = imageInfo.Height,
                Status = "Ready",
                SourceType = "Imported",
                MetadataJson = JsonSerializer.Serialize(new { character.CharacterKey, referenceType = "Front" }),
                CreatedAtUtc = now,
                VerifiedAtUtc = now
            };
            dbContext.MediaAssets.Add(asset);
            dbContext.CharacterReferences.Add(new CharacterReference
            {
                CharacterReferenceId = Guid.NewGuid(),
                CharacterId = character.CharacterId,
                MediaAssetId = asset.MediaAssetId,
                ReferenceType = "Front",
                IsPrimary = true,
                ApprovalStatus = "Approved",
                CreatedAtUtc = now,
                ApprovedAtUtc = now
            });
            character.Project.UpdatedAtUtc = now;
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            File.Delete(destinationPath);
            throw;
        }
    }

    public async Task ApproveCharacterAsync(
        Guid projectId,
        string remoteUserId,
        Guid characterId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var character = await dbContext.Characters
            .Include(x => x.Project)
            .Include(x => x.CharacterReferences)
            .ThenInclude(x => x.MediaAsset)
            .SingleOrDefaultAsync(
                x => x.CharacterId == characterId &&
                     x.ProjectId == projectId &&
                     x.Project.RemoteUserId == remoteUserId &&
                     x.Project.DeletedAtUtc == null,
                cancellationToken)
            ?? throw new ArgumentException("Không tìm thấy nhân vật trong dự án hiện tại.");
        if (character.Status != "Draft")
        {
            throw new ArgumentException("Nhân vật không còn ở trạng thái chờ duyệt.");
        }
        if (!character.CharacterReferences.Any(reference =>
                reference.IsPrimary &&
                reference.ApprovalStatus == "Approved" &&
                reference.MediaAsset.Status == "Ready" &&
                reference.MediaAsset.DeletedAtUtc == null))
        {
            throw new ArgumentException("Hãy chọn một ảnh tham chiếu hợp lệ trước khi khóa nhân vật.");
        }

        var now = DateTime.UtcNow;
        character.Status = "Approved";
        character.ApprovedAtUtc = now;
        character.Project.UpdatedAtUtc = now;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AiModelSummary>> ListAvailableModelsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.ProviderModels
            .AsNoTracking()
            .Where(x => x.IsEnabled && x.Provider.IsEnabled)
            .OrderByDescending(x => x.IsDefault)
            .ThenBy(x => x.Provider.DisplayName)
            .ThenBy(x => x.DisplayName)
            .Select(x => new AiModelSummary(
                x.Provider.ProviderCode,
                x.Provider.DisplayName,
                x.ModelCode,
                x.DisplayName,
                x.Modality,
                x.IsDefault))
            .Take(8)
            .ToListAsync(cancellationToken);
    }

    public async Task<ProjectSummary> CreateAsync(
        CreateProjectCommand command,
        UserProfileResponse owner,
        Guid remoteDeviceId,
        CancellationToken cancellationToken = default)
    {
        Validate(command);
        var projectId = Guid.NewGuid();
        var relativeWorkspace = workspaceService.Create(projectId);
        var now = DateTime.UtcNow;
        var project = new Project
        {
            ProjectId = projectId,
            OrganizationId = command.OrganizationId,
            CreatedByUserId = owner.UserId,
            RemoteUserId = owner.UserId,
            RemoteDeviceId = remoteDeviceId,
            OwnerDisplayNameSnapshot = owner.DisplayName,
            Name = command.Name.Trim(),
            Topic = command.Topic.Trim(),
            LanguageCode = command.LanguageCode,
            VoiceCode = string.IsNullOrWhiteSpace(command.VoiceCode) ? null : command.VoiceCode.Trim(),
            VoiceSpeakingRate = command.VoiceSpeakingRate,
            Platform = command.Platform,
            AspectRatio = command.AspectRatio,
            TargetDurationSeconds = command.TargetDurationSeconds,
            OutputWidth = command.AspectRatio == "16:9" ? 1920 : 1080,
            OutputHeight = command.AspectRatio == "16:9" ? 1080 : 1920,
            OutputFrameRate = 30,
            Status = "Draft",
            BudgetLimit = command.BudgetLimit,
            EstimatedCost = 0,
            ActualCost = 0,
            CurrencyCode = "USD",
            WorkspaceRelativePath = relativeWorkspace,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        dbContext.Projects.Add(project);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new ProjectSummary(
            project.ProjectId,
            project.OrganizationId,
            project.Name,
            project.Topic,
            project.Platform,
            project.AspectRatio,
            project.TargetDurationSeconds,
            project.Status,
            project.ActualCost,
            project.BudgetLimit,
            project.UpdatedAtUtc);
    }

    public async Task<ShortVideoProjectResult> CreateShortVideoAsync(
        CreateShortVideoCommand command,
        UserProfileResponse owner,
        Guid remoteDeviceId,
        CancellationToken cancellationToken = default)
    {
        Validate(command);
        var content = command.Content.Trim();
        var projectId = Guid.NewGuid();
        var conceptId = Guid.NewGuid();
        var scriptId = Guid.NewGuid();
        var styleProfileId = Guid.NewGuid();
        var sceneId = Guid.NewGuid();
        var scenePromptId = Guid.NewGuid();
        var relativeWorkspace = workspaceService.Create(projectId);
        var now = DateTime.UtcNow;
        var projectName = CreateShortVideoName(content);
        var providerDurationSeconds = command.DurationSeconds;
        var contentDurationMs = command.DurationSeconds * 1000L;
        var generationDurationMs = providerDurationSeconds * 1000L;
        var platform = command.AspectRatio switch
        {
            "9:16" => "YouTubeShorts",
            "1:1" => "InstagramReels",
            _ => "YouTube"
        };
        var (outputWidth, outputHeight) = command.AspectRatio switch
        {
            "16:9" => (1920, 1080),
            "1:1" => (1080, 1080),
            _ => (1080, 1920)
        };
        var canonicalInputJson = JsonSerializer.Serialize(new
        {
            workflow = "short-video-direct-prompt",
            content,
            durationSeconds = command.DurationSeconds,
            providerDurationSeconds,
            aspectRatio = command.AspectRatio,
            nativeAudio = true,
            outputAudioEnabled = command.AudioEnabled,
            muteOutputAudio = !command.AudioEnabled,
            speechMode = KlingSpeechModes.None
        });
        const string negativePrompt =
            "text overlays, subtitles, watermarks, logos, distorted anatomy, duplicated subjects, flicker, visual artifacts";

        var project = new Project
        {
            ProjectId = projectId,
            OrganizationId = command.OrganizationId,
            CreatedByUserId = owner.UserId,
            RemoteUserId = owner.UserId,
            RemoteDeviceId = remoteDeviceId,
            OwnerDisplayNameSnapshot = owner.DisplayName,
            Name = projectName,
            Topic = content,
            LanguageCode = "vi-VN",
            Platform = platform,
            AspectRatio = command.AspectRatio,
            TargetDurationSeconds = command.DurationSeconds,
            OutputWidth = outputWidth,
            OutputHeight = outputHeight,
            OutputFrameRate = 30,
            Status = "ScenePlanning",
            CurrentConceptVersion = 1,
            CurrentScriptVersion = 1,
            CurrentStyleVersion = 1,
            CurrentScenePlanVersion = 1,
            RequireContentApproval = false,
            RequireStoryboardApproval = false,
            EstimatedCost = 0,
            ActualCost = 0,
            CurrencyCode = "USD",
            WorkspaceRelativePath = relativeWorkspace,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        var concept = new Concept
        {
            ConceptId = conceptId,
            ProjectId = projectId,
            Version = 1,
            Title = projectName,
            Angle = "Direct short-video prompt supplied by the user.",
            StrategyJson = JsonSerializer.Serialize(new
            {
                workflow = "short-video-direct-prompt",
                usesOpenAi = false,
                durationSeconds = command.DurationSeconds,
                providerDurationSeconds
            }),
            Status = "Approved",
            CreatedAtUtc = now,
            ApprovedAtUtc = now
        };
        var script = new Script
        {
            ScriptId = scriptId,
            ProjectId = projectId,
            ConceptId = conceptId,
            Version = 1,
            StructureType = "DirectShortVideo",
            Title = projectName,
            FullText = content,
            NarrationJson = "[]",
            DialogueJson = "[]",
            StoryBeatsJson = JsonSerializer.Serialize(new[]
            {
                new { id = "short-video-scene", content, durationSeconds = command.DurationSeconds }
            }),
            EstimatedDurationMs = contentDurationMs,
            Status = "Approved",
            CreatedAtUtc = now,
            ApprovedAtUtc = now
        };
        var styleProfile = new StyleProfile
        {
            StyleProfileId = styleProfileId,
            ProjectId = projectId,
            Version = 1,
            Name = "Direct short video",
            VisualStyleJson = JsonSerializer.Serialize(new { description = content }),
            CameraStyleJson = JsonSerializer.Serialize(new { direction = "Follow the user's prompt." }),
            LightingStyleJson = JsonSerializer.Serialize(new { direction = "Follow the user's prompt." }),
            EnvironmentJson = JsonSerializer.Serialize(new
            {
                nativeAudio = command.AudioEnabled
                    ? "Natural environmental audio matching the scene."
                    : "Provider audio is removed from the local output."
            }),
            NegativeRulesJson = JsonSerializer.Serialize(new[] { negativePrompt }),
            Status = "Approved",
            CreatedAtUtc = now,
            ApprovedAtUtc = now
        };
        var scene = new Scene
        {
            SceneId = sceneId,
            ProjectId = projectId,
            ScriptId = scriptId,
            StyleProfileId = styleProfileId,
            ScenePlanVersion = 1,
            SequenceNumber = 1,
            StoryBeatId = "short-video-scene",
            StoryPurpose = $"Video ngắn {command.DurationSeconds} giây từ nội dung người dùng nhập.",
            VisualDescription = content,
            ContentDurationMs = contentDurationMs,
            GenerationDurationMs = generationDurationMs,
            TimelineStartMs = 0,
            TimelineEndMs = contentDurationMs,
            TailTrimMs = generationDurationMs - contentDurationMs,
            CharacterIdsJson = "[]",
            EntryStateJson = "{}",
            ExitStateJson = "{}",
            RequiredCapabilitiesJson = JsonSerializer.Serialize(new
            {
                textToVideo = true,
                maxDurationSeconds = 15,
                requestedDurationSeconds = command.DurationSeconds,
                providerDurationSeconds,
                aspectRatio = command.AspectRatio,
                nativeAudio = true,
                outputAudioEnabled = command.AudioEnabled,
                muteOutputAudio = !command.AudioEnabled,
                speechMode = KlingSpeechModes.None,
                ambientAudio = command.AudioEnabled
                    ? "Natural environmental audio matching the scene."
                    : null,
                soundEffects = command.AudioEnabled
                    ? "Natural sound effects synchronized with visible actions."
                    : null
            }),
            Status = "PromptReady",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        var prompt = new ScenePrompt
        {
            ScenePromptId = scenePromptId,
            SceneId = sceneId,
            Version = 1,
            PromptTemplateName = "manual-short-video",
            PromptTemplateVersion = "1",
            CanonicalInputJson = canonicalInputJson,
            FinalPrompt = content,
            NegativePrompt = negativePrompt,
            PromptHash = Sha256Hex(content + "\n" + negativePrompt + "\n" + canonicalInputJson),
            Status = "Approved",
            CreatedAtUtc = now,
            ApprovedAtUtc = now
        };

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        dbContext.AddRange(project, concept, script, styleProfile, scene, prompt);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new ShortVideoProjectResult(
            new ProjectSummary(
                project.ProjectId,
                project.OrganizationId,
                project.Name,
                project.Topic,
                project.Platform,
                project.AspectRatio,
                project.TargetDurationSeconds,
                project.Status,
                project.ActualCost,
                project.BudgetLimit,
                project.UpdatedAtUtc),
            sceneId);
    }

    private static void Validate(CreateProjectCommand command)
    {
        if (command.OrganizationId == Guid.Empty)
        {
            throw new ArgumentException("Tổ chức của project không hợp lệ.", nameof(command));
        }

        if (string.IsNullOrWhiteSpace(command.Name) || command.Name.Length > 300)
        {
            throw new ArgumentException("Tên project phải có từ 1 đến 300 ký tự.", nameof(command));
        }

        if (string.IsNullOrWhiteSpace(command.Topic) || command.Topic.Length > 2000)
        {
            throw new ArgumentException("Chủ đề phải có từ 1 đến 2000 ký tự.", nameof(command));
        }

        if (command.Platform is not ("TikTok" or "YouTubeShorts" or "InstagramReels" or "YouTube" or "Facebook"))
        {
            throw new ArgumentException("Platform không được hỗ trợ.", nameof(command));
        }

        if (command.AspectRatio is not ("9:16" or "16:9" or "1:1"))
        {
            throw new ArgumentException("Aspect ratio không được hỗ trợ.", nameof(command));
        }

        if (command.TargetDurationSeconds is < 5 or > 3600)
        {
            throw new ArgumentException("Thời lượng phải nằm trong khoảng 5–3600 giây.", nameof(command));
        }

        if (command.BudgetLimit < 0)
        {
            throw new ArgumentException("Ngân sách không được âm.", nameof(command));
        }

        if (string.IsNullOrWhiteSpace(command.LanguageCode) || command.LanguageCode.Length > 20)
        {
            throw new ArgumentException("Mã ngôn ngữ không hợp lệ.", nameof(command));
        }

        if (command.VoiceCode is not null && command.VoiceCode is not ("female-sweet" or "male-warm"))
        {
            throw new ArgumentException("Giọng đọc không được hỗ trợ.", nameof(command));
        }

        if (command.VoiceSpeakingRate is { } speakingRate && speakingRate is < 0.5m or > 2m)
        {
            throw new ArgumentException("Tốc độ giọng đọc phải nằm trong khoảng 0,5–2,0.", nameof(command));
        }
    }

    private static void Validate(CreateShortVideoCommand command)
    {
        if (command.OrganizationId == Guid.Empty)
        {
            throw new ArgumentException("Tổ chức của video ngắn không hợp lệ.", nameof(command));
        }
        if (string.IsNullOrWhiteSpace(command.Content) || command.Content.Trim().Length > 2000)
        {
            throw new ArgumentException("Nội dung video phải có từ 1 đến 2.000 ký tự.", nameof(command));
        }
        if (command.AspectRatio is not ("9:16" or "16:9" or "1:1"))
        {
            throw new ArgumentException("Tỷ lệ khung hình không được hỗ trợ.", nameof(command));
        }
        if (command.DurationSeconds is < 5 or > 15)
        {
            throw new ArgumentException("Thời lượng video phải nằm trong khoảng 5–15 giây.", nameof(command));
        }
    }

    private static string CreateShortVideoName(string content)
    {
        const int maximumLength = 70;
        var singleLine = string.Join(' ', content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return singleLine.Length <= maximumLength
            ? singleLine
            : $"{singleLine[..(maximumLength - 1)].TrimEnd()}…";
    }

    private static IReadOnlyList<PipelineStageSummary> CreatePipeline(
        IReadOnlyList<DashboardJob> jobs,
        long totalScenes,
        long approvedScenes,
        bool hasConcept,
        bool hasScript,
        int promptCount,
        bool hasVideoActivity,
        bool hasRenderActivity)
    {
        PipelineStageSummary[] stages =
        [
            CreateStage(
                "research",
                "Nghiên cứu viral",
                "Phân tích xu hướng",
                [JobTypes.AnalyzeTopic, JobTypes.GenerateConcept],
                jobs,
                ["Phân tích chủ đề và định hướng nội dung"]),
            CreateStage(
                "script",
                "Kịch bản AI",
                "Viết kịch bản hấp dẫn",
                [JobTypes.GenerateScript, JobTypes.GenerateCharacter, JobTypes.GenerateCharacterReference],
                jobs,
                ["Tạo kịch bản, nhân vật và tham chiếu"]),
            CreateStage(
                "scenes",
                "Chia cảnh",
                "Tạo danh sách cảnh",
                [JobTypes.GenerateScenePlan, JobTypes.GeneratePrompt],
                jobs,
                totalScenes > 0
                    ? [$"Tổng số cảnh: {totalScenes}", $"Cảnh đã duyệt: {approvedScenes}/{totalScenes}"]
                    : ["Chưa có kế hoạch cảnh"]),
            CreateStage(
                "video",
                "Tạo video",
                "Sinh video từ AI",
                [JobTypes.GenerateVideo, JobTypes.PollVideo, JobTypes.DownloadVideo, JobTypes.ValidateVideo],
                jobs,
                totalScenes > 0
                    ? [$"Cần tạo {totalScenes} video clip", $"Đã hoàn thành {approvedScenes} cảnh"]
                    : ["Chờ danh sách cảnh"]),
            CreateStage(
                "render",
                "Ghép video",
                "Hoàn thiện và xuất",
                [JobTypes.GenerateSubtitle, JobTypes.RenderFinalVideo],
                jobs,
                ["Ghép các cảnh và giữ Native Audio, sau đó xuất file"])
        ];

        return stages.Select(stage => stage.Code switch
        {
            "research" => WithArtifactState(stage, hasConcept, hasConcept ? 100 : 0),
            "script" => WithArtifactState(stage, hasScript, hasScript ? 100 : 0),
            "scenes" => WithArtifactState(
                stage,
                totalScenes > 0 && promptCount >= totalScenes,
                totalScenes > 0 ? Math.Min(100, promptCount * 100m / totalScenes) : 0),
            "video" => WithArtifactState(
                stage,
                totalScenes > 0 && approvedScenes >= totalScenes,
                totalScenes > 0 && hasVideoActivity ? approvedScenes * 100m / totalScenes : 0),
            "render" => WithArtifactState(stage, false, hasRenderActivity ? 1 : 0),
            _ => stage
        }).ToArray();
    }

    private static PipelineStageSummary WithArtifactState(
        PipelineStageSummary stage,
        bool completed,
        decimal progress)
    {
        if (stage.Status != "waiting")
        {
            return stage;
        }

        return stage with
        {
            Status = completed ? "completed" : progress > 0 ? "processing" : "waiting",
            ProgressPercent = completed ? 100 : progress
        };
    }

    private static PipelineStageSummary CreateStage(
        string code,
        string title,
        string subtitle,
        IReadOnlyCollection<string> jobTypes,
        IReadOnlyList<DashboardJob> jobs,
        IReadOnlyList<string> detailLines)
    {
        var stageJobs = jobs.Where(x => jobTypes.Contains(x.JobType, StringComparer.Ordinal)).ToArray();
        if (stageJobs.Length == 0)
        {
            return new PipelineStageSummary(code, title, subtitle, "waiting", 0, detailLines);
        }

        var status = stageJobs.Any(x => x.Status == JobStatuses.Failed)
            ? "failed"
            : stageJobs.Any(x => x.Status == JobStatuses.Running)
                ? "processing"
                : stageJobs.All(x => x.Status == JobStatuses.Completed)
                    ? "completed"
                    : "waiting";
        var progress = status == "completed"
            ? 100
            : Math.Round(stageJobs.Average(x => x.ProgressPercent), 2);
        return new PipelineStageSummary(code, title, subtitle, status, progress, detailLines);
    }

    private static IReadOnlyList<string> NormalizeCharacterRules(IReadOnlyList<string>? values, string fieldName)
    {
        var result = values?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
        if (result.Length is < 1 or > 12 || result.Any(value => value.Length > 500))
        {
            throw new ArgumentException($"{fieldName} phải có từ 1 đến 12 mục hợp lệ.");
        }

        return result;
    }

    private static async Task<CharacterImageInfo> ValidateCharacterImageAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var header = new byte[12];
        await using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            var read = await stream.ReadAsync(header, cancellationToken);
            if (read < 8)
            {
                throw new ArgumentException("Ảnh tham chiếu không đúng định dạng JPEG hoặc PNG.");
            }
        }

        var isPng = header.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        var isJpeg = header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF;
        if (!isPng && !isJpeg)
        {
            throw new ArgumentException("Ảnh tham chiếu không đúng định dạng JPEG hoặc PNG.");
        }

        int width;
        int height;
        try
        {
            using var image = System.Drawing.Image.FromFile(path);
            width = image.Width;
            height = image.Height;
        }
        catch (Exception exception) when (exception is ArgumentException or OutOfMemoryException)
        {
            throw new ArgumentException("Không thể đọc nội dung ảnh tham chiếu.", exception);
        }

        var aspectRatio = (decimal)width / height;
        if (width < 300 || height < 300 || aspectRatio is < 0.4m or > 2.5m)
        {
            throw new ArgumentException("Ảnh tham chiếu phải từ 300x300 px và có tỷ lệ trong khoảng 1:2.5 đến 2.5:1.");
        }

        return new CharacterImageInfo(
            width,
            height,
            isPng ? "image/png" : "image/jpeg",
            isPng ? ".png" : ".jpg");
    }

    private static IReadOnlyList<Guid> ParseGuidList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<Guid[]>(json)?.Distinct().ToArray() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string MergeNativeAudioCapabilities(
        string? json,
        string speechMode,
        string? voiceStyle,
        string? ambientAudio,
        string? soundEffects)
    {
        JsonObject root;
        try
        {
            root = string.IsNullOrWhiteSpace(json)
                ? new JsonObject()
                : JsonNode.Parse(json) as JsonObject ?? new JsonObject();
        }
        catch (JsonException)
        {
            root = new JsonObject();
        }

        root["nativeAudio"] = true;
        root["speechMode"] = speechMode;
        root["voiceStyle"] = voiceStyle;
        root["ambientAudio"] = ambientAudio;
        root["soundEffects"] = soundEffects;
        return root.ToJsonString();
    }

    private static string? NormalizeOptionalSceneAudioText(string? value, int maximumLength, string fieldName)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (normalized?.Length > maximumLength)
        {
            throw new ArgumentException($"{fieldName} không được vượt quá {maximumLength:N0} ký tự.");
        }
        return normalized;
    }

    private static string? ReadStringProperty(string? json, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty(propertyName, out var value) &&
                   value.ValueKind == JsonValueKind.String &&
                   !string.IsNullOrWhiteSpace(value.GetString())
                ? value.GetString()!.Trim()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool ReadBooleanProperty(string? json, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty(propertyName, out var value) &&
                   value.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static IReadOnlyList<string> ParseStringList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<string[]>(json)?
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IReadOnlyList<string> ReadStringArrayProperty(string json, string propertyName)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty(propertyName, out var values) || values.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return values.EnumerateArray()
                .Where(value => value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()))
                .Select(value => value.GetString()!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string FormatWardrobe(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return "Chưa có mô tả trang phục.";
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var parts = new List<string>();
            foreach (var propertyName in new[] { "clothing", "accessories" })
            {
                if (document.RootElement.TryGetProperty(propertyName, out var value) &&
                    value.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(value.GetString()))
                {
                    parts.Add(value.GetString()!.Trim());
                }
            }

            return parts.Count == 0 ? "Chưa có mô tả trang phục." : string.Join("; ", parts);
        }
        catch (JsonException)
        {
            return "Chưa có mô tả trang phục.";
        }
    }

    private VideoPreviewSummary? CreatePreview(string projectWorkspace, PreviewAsset? asset)
    {
        var sourcePath = asset?.ExportedPath ?? asset?.RelativePath;
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return null;
        }

        try
        {
            string workspaceRelativePath;
            if (Path.IsPathRooted(sourcePath))
            {
                var fullPath = Path.GetFullPath(sourcePath);
                var rootWithSeparator = workspaceService.WorkspaceRoot.TrimEnd(Path.DirectorySeparatorChar) +
                                        Path.DirectorySeparatorChar;
                if (!fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                workspaceRelativePath = Path.GetRelativePath(workspaceService.WorkspaceRoot, fullPath);
            }
            else
            {
                var normalized = sourcePath.Replace('/', Path.DirectorySeparatorChar);
                var projectPrefix = projectWorkspace.Replace('/', Path.DirectorySeparatorChar)
                    .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                workspaceRelativePath = normalized.StartsWith(projectPrefix, StringComparison.OrdinalIgnoreCase)
                    ? normalized
                    : Path.Combine(projectWorkspace.Replace('/', Path.DirectorySeparatorChar), normalized);
            }

            var resolved = workspaceService.Resolve(workspaceRelativePath);
            if (!File.Exists(resolved))
            {
                return null;
            }

            var urlPath = string.Join(
                '/',
                workspaceRelativePath
                    .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries)
                    .Select(Uri.EscapeDataString));
            return new VideoPreviewSummary(
                $"https://media.app.local/{urlPath}",
                asset?.DurationMs,
                asset?.MimeType);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return null;
        }
    }

    private sealed record DashboardJob(string JobType, string Status, decimal ProgressPercent);

    private sealed record PreviewAsset(
        string? ExportedPath,
        string RelativePath,
        long? DurationMs,
        string? MimeType);

    private sealed record SceneAsset(
        Guid SceneId,
        string RelativePath,
        long? DurationMs,
        string MimeType,
        string AssetType,
        string? MetadataJson);

    private sealed record CharacterImageInfo(
        int Width,
        int Height,
        string MimeType,
        string Extension);

    private static bool IsMediaToolError(string? code) =>
        code is "media_tool_unavailable" or
            "ffmpeg_not_found" or
            "ffprobe_not_found" or
            "media_tool_bundle_invalid" or
            "media_tool_not_executable" or
            "media_tool_version_check_failed" or
            "media_tool_version_mismatch";

    private static bool IsNativeAudioError(string? code) =>
        code is "audio_stream_missing" or
            "audio_duration_invalid" or
            "audio_levels_unavailable" or
            "audio_effectively_silent" or
            "kling_native_audio_missing" or
            "kling_native_audio_inaudible";

    private static bool IsVideoProviderRetryError(string? code) =>
        code is "provider_output_download_failed" or "provider_status_check_failed";

    private static string Sha256Hex(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
