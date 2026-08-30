using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TOOL_LOCAL.Authentication;
using TOOL_LOCAL.Data;
using TOOL_LOCAL.Data.Models;
using TOOL_LOCAL.Media;
using TOOL_LOCAL.Storage;
using TOOL_SHARED.Contracts.Generation;

namespace TOOL_LOCAL.Generation;

internal sealed class ProjectGenerationService(
    IDbContextFactory<VideoFactoryDbContext> dbContextFactory,
    ProjectWorkspaceService workspaceService,
    IGenerationClient apiClient,
    FfprobeService mediaProbe,
    IMediaToolPreflightService mediaToolPreflight,
    AudioQualityValidator audioQualityValidator,
    SceneAudioMixer sceneAudioMixer,
    SceneVideoTrimmer sceneVideoTrimmer) : IProjectGenerationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public Task<GenerationProviderStatusResponse> GetProviderStatusAsync(CancellationToken cancellationToken) =>
        apiClient.GetProviderStatusAsync(cancellationToken);

    public async Task<GeneratedContentResponse> GenerateContentAsync(
        Guid projectId,
        string remoteUserId,
        CancellationToken cancellationToken)
    {
        int version;
        string idempotencyKey;
        await using (var readContext = await dbContextFactory.CreateDbContextAsync(cancellationToken))
        {
            await RequireProjectAsync(readContext, projectId, remoteUserId, cancellationToken);
            version = (await readContext.Scripts
                .Where(x => x.ProjectId == projectId)
                .MaxAsync(x => (int?)x.Version, cancellationToken) ?? 0) + 1;
            var keyPrefix = $"content:{projectId:N}:v{version}";
            var failedAttempts = await readContext.ProviderRequests
                .AsNoTracking()
                .CountAsync(
                    x => x.ProjectId == projectId &&
                         x.RequestKind == "Text" &&
                         x.Status == "Failed" &&
                         x.IdempotencyKey.StartsWith(keyPrefix),
                    cancellationToken);
            idempotencyKey = failedAttempts == 0
                ? keyPrefix
                : $"{keyPrefix}:retry:{failedAttempts}";
        }

        var response = await apiClient.GenerateContentAsync(
            new GenerateContentRequest(projectId, idempotencyKey),
            cancellationToken);
        ValidateContentPlan(response.Plan);
        await PersistContentPlanAsync(projectId, remoteUserId, version, response, cancellationToken);
        return response;
    }

    public async Task<GenerateCharacterReferenceImageResponse> GenerateCharacterReferenceImageAsync(
        Guid projectId,
        string remoteUserId,
        Guid characterId,
        CancellationToken cancellationToken)
    {
        if (characterId == Guid.Empty)
        {
            throw new ArgumentException("Nhân vật cần tạo ảnh không hợp lệ.");
        }

        string projectWorkspace;
        string characterKey;
        string characterName;
        await using (var readContext = await dbContextFactory.CreateDbContextAsync(cancellationToken))
        {
            var project = await RequireProjectAsync(readContext, projectId, remoteUserId, cancellationToken);
            var character = await readContext.Characters.AsNoTracking().SingleOrDefaultAsync(
                x => x.CharacterId == characterId && x.ProjectId == projectId,
                cancellationToken)
                ?? throw new ArgumentException("Không tìm thấy nhân vật trong dự án hiện tại.");
            if (character.Status != "Draft")
            {
                throw new ArgumentException("Nhân vật đã khóa nên không thể tạo hoặc sinh lại ảnh.");
            }
            projectWorkspace = project.WorkspaceRelativePath;
            characterKey = character.CharacterKey;
            characterName = character.Name;
        }

        var response = await apiClient.GenerateCharacterReferenceImageAsync(
            new GenerateCharacterReferenceImageRequest(
                projectId,
                characterId,
                $"character-image:{characterId:N}:{Guid.NewGuid():N}"),
            cancellationToken);
        ValidateImageMetadata(response);

        await using (var existingContext = await dbContextFactory.CreateDbContextAsync(cancellationToken))
        {
            if (await existingContext.MediaAssets.AsNoTracking().AnyAsync(
                x => x.SourceProviderRequestId == response.ProviderRequestId,
                cancellationToken))
            {
                return response;
            }
        }

        var fileName = $"character-{characterKey}-{response.ProviderRequestId:N}.png";
        var assetRelativePath = Path.Combine("characters", fileName).Replace(Path.DirectorySeparatorChar, '/');
        var workspaceRelativePath = Path.Combine(projectWorkspace, "characters", fileName);
        var finalPath = workspaceService.Resolve(workspaceRelativePath);
        var partialPath = $"{finalPath}.part";
        Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
        if (File.Exists(partialPath))
        {
            File.Delete(partialPath);
        }

        var movedToFinal = false;
        try
        {
            await apiClient.DownloadCharacterImageAsync(response, partialPath, cancellationToken);
            await ValidateDownloadedCharacterImageAsync(partialPath, response, cancellationToken);
            File.Move(partialPath, finalPath, false);
            movedToFinal = true;

            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
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

            var duplicate = await dbContext.MediaAssets.SingleOrDefaultAsync(
                x => x.SourceProviderRequestId == response.ProviderRequestId,
                cancellationToken);
            if (duplicate is null)
            {
                foreach (var current in character.CharacterReferences.Where(x => x.IsPrimary))
                {
                    current.IsPrimary = false;
                }

                var now = DateTime.UtcNow;
                var asset = new MediaAsset
                {
                    MediaAssetId = Guid.NewGuid(),
                    ProjectId = projectId,
                    AssetType = "CharacterReference",
                    DisplayName = $"Ảnh AI tham chiếu {characterName}",
                    RelativePath = assetRelativePath,
                    MimeType = response.MimeType,
                    SizeBytes = response.SizeBytes,
                    Sha256 = response.Sha256,
                    Width = response.Width,
                    Height = response.Height,
                    Status = "Ready",
                    SourceType = "Generated",
                    SourceProviderCode = response.ProviderCode,
                    SourceProviderRequestId = response.ProviderRequestId,
                    MetadataJson = JsonSerializer.Serialize(new
                    {
                        character.CharacterKey,
                        referenceType = "Front",
                        response.ModelCode,
                        response.ProviderRequestId
                    }, JsonOptions),
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
            await transaction.CommitAsync(cancellationToken);
            return response;
        }
        catch
        {
            if (movedToFinal && File.Exists(finalPath))
            {
                File.Delete(finalPath);
            }
            throw;
        }
        finally
        {
            if (File.Exists(partialPath))
            {
                File.Delete(partialPath);
            }
        }
    }

    private static void ValidateImageMetadata(GenerateCharacterReferenceImageResponse response)
    {
        if (response.ProviderRequestId == Guid.Empty ||
            response.ProviderCode != "openai" ||
            response.ModelCode != "gpt-image-2" ||
            response.MimeType != "image/png" ||
            response.Width != 1024 ||
            response.Height != 1024 ||
            response.SizeBytes is <= 0 or > 10 * 1024 * 1024 ||
            response.Sha256.Length != 64 ||
            response.Sha256.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidDataException("Metadata ảnh GPT-Image-2 không hợp lệ.");
        }
    }

    private static async Task ValidateDownloadedCharacterImageAsync(
        string path,
        GenerateCharacterReferenceImageResponse response,
        CancellationToken cancellationToken)
    {
        var fileInfo = new FileInfo(path);
        if (!fileInfo.Exists || fileInfo.Length != response.SizeBytes)
        {
            throw new InvalidDataException("Ảnh GPT-Image-2 tải về không đúng dung lượng.");
        }

        var header = new byte[24];
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await stream.ReadExactlyAsync(header, cancellationToken);
        ReadOnlySpan<byte> pngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        var width = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(20, 4));
        if (!header.AsSpan(0, 8).SequenceEqual(pngSignature) ||
            width != response.Width ||
            height != response.Height)
        {
            throw new InvalidDataException("Chữ ký MIME hoặc kích thước ảnh GPT-Image-2 không hợp lệ.");
        }
    }

    public async Task<int> GenerateVideosAsync(
        Guid projectId,
        string remoteUserId,
        IReadOnlyCollection<Guid>? sceneIds,
        Func<string, CancellationToken, Task>? reportProgress,
        CancellationToken cancellationToken)
    {
        var requestedSceneIds = sceneIds?
            .Where(x => x != Guid.Empty)
            .Distinct()
            .ToArray();
        if (sceneIds is not null && (requestedSceneIds is null || requestedSceneIds.Length == 0))
        {
            throw new ArgumentException("Hãy chọn ít nhất một cảnh để tạo video.");
        }
        if (requestedSceneIds?.Length > 100)
        {
            throw new ArgumentException("Mỗi lần chỉ được tạo tối đa 100 cảnh.");
        }

        try
        {
            // Local media verification is mandatory after the provider completes. Check it
            // before any provider submit so a missing executable cannot create an
            // avoidable paid request.
            await mediaToolPreflight.RequireReadyAsync(cancellationToken);
        }
        catch (MediaToolUnavailableException exception)
        {
            await MarkMediaToolBlockedAsync(
                projectId,
                remoteUserId,
                requestedSceneIds,
                exception,
                cancellationToken);
            throw;
        }

        IReadOnlyList<SceneWorkItem> scenes;
        await using (var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken))
        {
            var project = await RequireProjectAsync(dbContext, projectId, remoteUserId, cancellationToken);
            var planVersion = project.CurrentScenePlanVersion
                ?? throw new ArgumentException("Hãy tạo nội dung OpenAI trước khi tạo video.");
            var sceneQuery = dbContext.Scenes
                .AsNoTracking()
                .Where(x => x.ProjectId == projectId && x.ScenePlanVersion == planVersion);
            if (requestedSceneIds is not null)
            {
                sceneQuery = sceneQuery.Where(x => requestedSceneIds.Contains(x.SceneId));
            }

            var rawScenes = await sceneQuery
                .OrderBy(x => x.SequenceNumber)
                .Select(x => new SceneWorkItem(
                    x.SceneId,
                    x.ScriptId,
                    x.ScenePlanVersion,
                    x.SequenceNumber,
                    x.ContentDurationMs,
                    x.GenerationDurationMs,
                    x.Narration,
                    x.Dialogue,
                    x.RequiredCapabilitiesJson,
                    x.Status,
                    x.CharacterIdsJson,
                    x.ScenePrompts
                        .OrderByDescending(prompt => prompt.Version)
                        .Select(prompt => new PromptWorkItem(
                            prompt.ScenePromptId,
                            prompt.Version,
                            prompt.FinalPrompt,
                            prompt.NegativePrompt))
                        .FirstOrDefault()))
                .ToListAsync(cancellationToken);
            var characterIdsByScene = rawScenes.ToDictionary(
                scene => scene.SceneId,
                scene => ParseGuidList(scene.CharacterIdsJson));
            if (characterIdsByScene.Values.Any(ids => ids.Count > 1))
            {
                throw new ArgumentException("Workflow video hiện hỗ trợ tối đa một nhân vật tham chiếu trong mỗi cảnh.");
            }

            var characterIds = characterIdsByScene.Values.SelectMany(ids => ids).Distinct().ToArray();
            var characterRows = characterIds.Length == 0
                ? []
                : await dbContext.Characters
                    .AsNoTracking()
                    .Where(character => character.ProjectId == projectId && characterIds.Contains(character.CharacterId))
                    .Select(character => new CharacterWorkItem(
                        character.CharacterId,
                        character.Name,
                        character.Status,
                        character.CharacterReferences
                            .Where(reference =>
                                reference.IsPrimary &&
                                reference.ApprovalStatus == "Approved" &&
                                reference.MediaAsset.Status == "Ready" &&
                                reference.MediaAsset.DeletedAtUtc == null)
                            .OrderByDescending(reference => reference.CreatedAtUtc)
                            .Select(reference => new ReferenceWorkItem(
                                reference.CharacterReferenceId,
                                reference.MediaAsset.RelativePath,
                                reference.MediaAsset.MimeType,
                                reference.MediaAsset.Sha256,
                                reference.MediaAsset.SizeBytes))
                            .FirstOrDefault()))
                    .ToListAsync(cancellationToken);
            var characterById = characterRows.ToDictionary(character => character.CharacterId);
            scenes = rawScenes
                .Select(scene =>
                {
                    var ids = characterIdsByScene[scene.SceneId];
                    return scene with
                    {
                        Character = ids.Count == 0 || !characterById.TryGetValue(ids[0], out var character)
                            ? null
                            : character
                    };
                })
                .ToArray();
            if (scenes.Count == 0 || scenes.Any(x => x.Prompt is null))
            {
                throw new ArgumentException("Dự án chưa có đủ prompt cho các cảnh.");
            }
            if (requestedSceneIds is not null && scenes.Count != requestedSceneIds.Length)
            {
                throw new ArgumentException("Danh sách cảnh chứa cảnh không thuộc kế hoạch hiện hành.");
            }

            var sceneById = scenes.ToDictionary(scene => scene.SceneId);
            if (characterIdsByScene.Any(pair => pair.Value.Count > 0 && sceneById[pair.Key].Character is null) ||
                scenes.Any(scene => scene.Character is { Status: not "Approved" } || scene.Character is { Reference: null }))
            {
                throw new ArgumentException("Hãy khóa nhân vật và chọn ảnh tham chiếu trước khi tạo video.");
            }

            var sceneWithSpeechOverBudget = scenes.FirstOrDefault(scene =>
                !string.IsNullOrWhiteSpace(scene.SpokenText) &&
                NativeSpeechWordBudget.CountWords(scene.SpokenText) >
                NativeSpeechWordBudget.MaximumWordsForDurationSeconds(
                    checked((int)Math.Ceiling(scene.GenerationDurationMs / 1000m))));
            if (sceneWithSpeechOverBudget is not null)
            {
                var wordCount = NativeSpeechWordBudget.CountWords(sceneWithSpeechOverBudget.SpokenText);
                var maximumWords = NativeSpeechWordBudget.MaximumWordsForDurationSeconds(
                    checked((int)Math.Ceiling(sceneWithSpeechOverBudget.GenerationDurationMs / 1000m)));
                throw new ArgumentException(
                    $"Cảnh {sceneWithSpeechOverBudget.SequenceNumber} có {wordCount} từ, vượt mức {maximumWords} từ. Hãy rút ngắn và lưu lời cảnh trước khi tạo clip.");
            }

            project.Status = "GeneratingScenes";
            project.LastErrorCode = null;
            project.LastErrorMessage = null;
            project.UpdatedAtUtc = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var completed = 0;
        try
        {
            foreach (var scene in scenes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (await IsSceneApprovedAsync(scene.SceneId, cancellationToken))
                {
                    completed++;
                    continue;
                }

                if (reportProgress is not null)
                {
                    await reportProgress(
                        $"Đang tạo video cảnh {scene.SequenceNumber}/{scenes.Count}...",
                        cancellationToken);
                }

                await GenerateSceneAsync(projectId, remoteUserId, scene, cancellationToken);
                completed++;
                if (reportProgress is not null)
                {
                    await reportProgress(
                        $"Đã tải xong cảnh {scene.SequenceNumber}/{scenes.Count}.",
                        cancellationToken);
                }
            }

            var allScenesApproved = await AreAllScenesApprovedAsync(projectId, cancellationToken);
            await UpdateProjectStatusAsync(
                projectId,
                remoteUserId,
                allScenesApproved ? "ReadyToRender" : "ScenePlanning",
                null,
                null,
                cancellationToken);
            return completed;
        }
        catch (MediaToolUnavailableException exception)
        {
            await MarkMediaToolBlockedAsync(
                projectId,
                remoteUserId,
                requestedSceneIds,
                exception,
                CancellationToken.None);
            throw;
        }
        catch (AccountClientException exception) when (IsSpeechWordBudgetError(exception.Code))
        {
            await UpdateProjectStatusAsync(
                projectId,
                remoteUserId,
                "ScenePlanning",
                SafeCode(exception.Code),
                SafeMessage(exception.Message),
                CancellationToken.None);
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var code = exception is AccountClientException accountException
                ? accountException.Code
                : "video_generation_failed";
            await UpdateProjectStatusAsync(
                projectId,
                remoteUserId,
                "Failed",
                SafeCode(code),
                SafeMessage(exception.Message),
                cancellationToken);
            throw;
        }
    }

    private async Task PersistContentPlanAsync(
        Guid projectId,
        string remoteUserId,
        int version,
        GeneratedContentResponse response,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var project = await RequireProjectAsync(dbContext, projectId, remoteUserId, cancellationToken);
        if (project.CurrentScriptVersion is >= 1 && project.CurrentScriptVersion >= version)
        {
            return;
        }

        var now = DateTime.UtcNow;
        await dbContext.Concepts
            .Where(x => x.ProjectId == projectId && x.Status == "Approved")
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Status, "Superseded"), cancellationToken);
        await dbContext.Scripts
            .Where(x => x.ProjectId == projectId && x.Status == "Approved")
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Status, "Superseded"), cancellationToken);
        await dbContext.StyleProfiles
            .Where(x => x.ProjectId == projectId && x.Status == "Approved")
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Status, "Superseded"), cancellationToken);
        await dbContext.Characters
            .Where(x => x.ProjectId == projectId && (x.Status == "Approved" || x.Status == "Draft"))
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Status, "Superseded"), cancellationToken);

        var concept = new Concept
        {
            ConceptId = Guid.NewGuid(),
            ProjectId = projectId,
            Version = version,
            Title = response.Plan.Title,
            SelectedHook = response.Plan.Hook,
            Angle = response.Plan.Angle,
            Audience = response.Plan.Audience,
            CallToAction = response.Plan.CallToAction,
            HooksJson = JsonSerializer.Serialize(new[] { response.Plan.Hook }, JsonOptions),
            StrategyJson = JsonSerializer.Serialize(new
            {
                response.Plan.Angle,
                response.Plan.Audience,
                response.Plan.VisualStyle
            }, JsonOptions),
            Status = "Approved",
            ProviderCode = response.ProviderCode,
            ModelCode = response.ModelCode,
            CreatedAtUtc = now,
            ApprovedAtUtc = now
        };
        var script = new Script
        {
            ScriptId = Guid.NewGuid(),
            ProjectId = projectId,
            ConceptId = concept.ConceptId,
            Version = version,
            StructureType = "OpenAiStructuredPlan",
            Title = response.Plan.Title,
            FullText = response.Plan.ScriptFullText,
            NarrationJson = JsonSerializer.Serialize(response.Plan.Scenes.Select(x => x.Narration), JsonOptions),
            StoryBeatsJson = JsonSerializer.Serialize(response.Plan.Scenes, JsonOptions),
            EstimatedDurationMs = response.Plan.Scenes.Sum(x => (long)x.DurationSeconds * 1000),
            Status = "Approved",
            ProviderCode = response.ProviderCode,
            ModelCode = response.ModelCode,
            CreatedAtUtc = now,
            ApprovedAtUtc = now
        };
        var style = new StyleProfile
        {
            StyleProfileId = Guid.NewGuid(),
            ProjectId = projectId,
            Version = version,
            Name = $"OpenAI Style v{version}",
            VisualStyleJson = JsonSerializer.Serialize(new { description = response.Plan.VisualStyle }, JsonOptions),
            NegativeRulesJson = JsonSerializer.Serialize(new { prompt = response.Plan.NegativePrompt }, JsonOptions),
            Status = "Approved",
            CreatedAtUtc = now,
            ApprovedAtUtc = now
        };
        dbContext.Concepts.Add(concept);
        dbContext.Scripts.Add(script);
        dbContext.StyleProfiles.Add(style);

        var characters = response.Plan.Characters
            .Select(generated => new Character
            {
                CharacterId = Guid.NewGuid(),
                ProjectId = projectId,
                CharacterKey = generated.CharacterKey,
                Version = version,
                Name = generated.Name,
                Role = generated.Role,
                IdentityAnchor = generated.CharacterKey,
                ProfileJson = JsonSerializer.Serialize(generated, JsonOptions),
                WardrobeJson = JsonSerializer.Serialize(new
                {
                    generated.Clothing,
                    generated.Accessories
                }, JsonOptions),
                ForbiddenChangesJson = JsonSerializer.Serialize(generated.ForbiddenChanges, JsonOptions),
                VisualIdentity = generated.VisualIdentity,
                Status = "Draft",
                CreatedAtUtc = now
            })
            .ToArray();
        var characterByKey = characters.ToDictionary(x => x.CharacterKey, StringComparer.OrdinalIgnoreCase);
        dbContext.Characters.AddRange(characters);

        long timelineMs = 0;
        var scenes = new List<Scene>();
        foreach (var generatedScene in response.Plan.Scenes.OrderBy(x => x.SequenceNumber))
        {
            var sceneId = Guid.NewGuid();
            var durationMs = generatedScene.DurationSeconds * 1000L;
            var scene = new Scene
            {
                SceneId = sceneId,
                ProjectId = projectId,
                ScriptId = script.ScriptId,
                StyleProfileId = style.StyleProfileId,
                ScenePlanVersion = version,
                SequenceNumber = generatedScene.SequenceNumber,
                ContinuityGroupKey = "main-story",
                StoryBeatId = $"scene-{generatedScene.SequenceNumber:000}",
                StoryPurpose = generatedScene.StoryPurpose,
                Narration = ResolveSpeechMode(generatedScene) == KlingSpeechModes.NativeVoiceOver
                    ? NullIfWhiteSpace(generatedScene.Narration)
                    : null,
                Dialogue = ResolveSpeechMode(generatedScene) == KlingSpeechModes.OnCameraDialogue
                    ? NullIfWhiteSpace(generatedScene.Narration)
                    : null,
                VisualDescription = generatedScene.VisualPrompt,
                CameraDirection = "cinematic composition and controlled camera movement",
                Lighting = response.Plan.VisualStyle,
                Motion = "natural coherent subject motion",
                ContentDurationMs = durationMs,
                GenerationDurationMs = durationMs,
                TimelineStartMs = timelineMs,
                TimelineEndMs = timelineMs + durationMs,
                CharacterIdsJson = JsonSerializer.Serialize(
                    generatedScene.CharacterKeys.Select(key => characterByKey[key].CharacterId),
                    JsonOptions),
                EntryStateJson = JsonSerializer.Serialize(new { continuity = "continue from previous scene" }, JsonOptions),
                ExitStateJson = JsonSerializer.Serialize(new { continuity = "prepare for next scene" }, JsonOptions),
                RequiredCapabilitiesJson = JsonSerializer.Serialize(new
                {
                    textToVideo = true,
                    maxDurationSeconds = 15,
                    project.AspectRatio,
                    nativeAudio = true,
                    speechMode = ResolveSpeechMode(generatedScene),
                    generatedScene.SpeakerCharacterKey,
                    generatedScene.VoiceStyle,
                    generatedScene.AmbientAudio,
                    generatedScene.SoundEffects
                }, JsonOptions),
                Status = "PromptReady",
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            var prompt = new ScenePrompt
            {
                ScenePromptId = Guid.NewGuid(),
                SceneId = sceneId,
                Version = 1,
                PromptTemplateName = "openai-content-plan",
                PromptTemplateVersion = "2",
                CanonicalInputJson = JsonSerializer.Serialize(generatedScene, JsonOptions),
                FinalPrompt = generatedScene.VisualPrompt,
                NegativePrompt = response.Plan.NegativePrompt,
                PromptHash = Sha256Hex(generatedScene.VisualPrompt + "\n" + response.Plan.NegativePrompt),
                Status = "Approved",
                CreatedAtUtc = now,
                ApprovedAtUtc = now
            };
            scene.ScenePrompts.Add(prompt);
            scenes.Add(scene);
            timelineMs += durationMs;
        }

        dbContext.Scenes.AddRange(scenes);
        project.CurrentConceptVersion = version;
        project.CurrentScriptVersion = version;
        project.CurrentCharacterVersion = version;
        project.CurrentStyleVersion = version;
        project.CurrentScenePlanVersion = version;
        project.Status = "ScenePlanning";
        project.LastErrorCode = null;
        project.LastErrorMessage = null;
        project.UpdatedAtUtc = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        for (var index = 0; index < scenes.Count; index++)
        {
            scenes[index].PreviousSceneId = index == 0 ? null : scenes[index - 1].SceneId;
            scenes[index].NextSceneId = index == scenes.Count - 1 ? null : scenes[index + 1].SceneId;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var relativePath = Path.Combine(project.WorkspaceRelativePath, "script", $"content-plan-v{version}.json");
        var outputPath = workspaceService.Resolve(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await File.WriteAllTextAsync(
            outputPath,
            JsonSerializer.Serialize(response, JsonOptions),
            Encoding.UTF8,
            cancellationToken);
    }

    private async Task GenerateSceneAsync(
        Guid projectId,
        string remoteUserId,
        SceneWorkItem scene,
        CancellationToken cancellationToken)
    {
        var prompt = scene.Prompt!;
        VideoTaskResponse task;
        await using (var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken))
        {
            var project = await RequireProjectAsync(dbContext, projectId, remoteUserId, cancellationToken);
            var existingRequest = await dbContext.ProviderRequests
                .AsNoTracking()
                .Where(x => x.ProjectId == projectId && x.SceneId == scene.SceneId && x.RequestKind == "Video")
                .OrderByDescending(x => x.CreatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);
            var requiresNewAttempt = scene.Status == "NativeAudioInvalid";
            if (!requiresNewAttempt &&
                existingRequest is not null &&
                existingRequest.Status is not ("Failed" or "Cancelled" or "Expired"))
            {
                task = await apiClient.GetVideoStatusAsync(existingRequest.ProviderRequestId, cancellationToken);
            }
            else
            {
                var referenceImage = scene.Character?.Reference is null
                    ? null
                    : await LoadReferenceImageAsync(
                        project.WorkspaceRelativePath,
                        scene.Character.Reference,
                        cancellationToken);
                var attempt = await dbContext.ProviderRequests.CountAsync(
                    x => x.ProjectId == projectId && x.SceneId == scene.SceneId && x.RequestKind == "Video",
                    cancellationToken) + 1;
                try
                {
                    task = await apiClient.SubmitVideoAsync(
                        new SubmitVideoRequest(
                            projectId,
                            scene.SceneId,
                            $"video:{prompt.ScenePromptId:N}:ref:{referenceImage?.CharacterReferenceId.ToString("N") ?? "none"}:attempt:{attempt}",
                            ReferenceImage: referenceImage,
                            ScenePlanVersion: scene.ScenePlanVersion,
                            ScenePromptVersion: prompt.Version),
                        cancellationToken);
                }
                catch (AccountClientException exception) when (IsSpeechWordBudgetError(exception.Code))
                {
                    await MarkSceneSpeechValidationFailedAsync(
                        projectId,
                        remoteUserId,
                        scene.SceneId,
                        exception,
                        CancellationToken.None);
                    throw;
                }
            }
        }

        var generationId = await EnsureVideoGenerationAsync(scene, task, cancellationToken);
        var deadline = DateTime.UtcNow.AddMinutes(45);
        while (task.Status is not ("Completed" or "Failed" or "Cancelled" or "Expired"))
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("Provider chưa hoàn tất cảnh sau 45 phút; bạn có thể tiếp tục kiểm tra task sau.");
            }

            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            task = await apiClient.GetVideoStatusAsync(task.ProviderRequestId, cancellationToken);
            await UpdateGenerationWaitingStatusAsync(
                generationId,
                scene.SceneId,
                task.Status,
                task.ErrorCode,
                task.ErrorMessage,
                cancellationToken);
        }

        if (task.Status != "Completed" || string.IsNullOrWhiteSpace(task.OutputUrl))
        {
            throw new AccountClientException(
                task.ErrorCode ?? "video_generation_failed",
                task.ErrorMessage ?? "Provider không thể hoàn tất video.",
                502);
        }

        await DownloadAndApproveAsync(projectId, remoteUserId, scene, generationId, task, cancellationToken);
    }

    private async Task<VideoReferenceImageInput> LoadReferenceImageAsync(
        string projectWorkspace,
        ReferenceWorkItem reference,
        CancellationToken cancellationToken)
    {
        if (reference.SizeBytes is <= 0 or > 10 * 1024 * 1024 ||
            reference.MimeType is not ("image/jpeg" or "image/png"))
        {
            throw new InvalidDataException("Ảnh tham chiếu nhân vật không đáp ứng giới hạn của provider video.");
        }

        var relativePath = Path.Combine(
            projectWorkspace.Replace('/', Path.DirectorySeparatorChar),
            reference.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        var resolvedPath = workspaceService.Resolve(relativePath);
        if (!File.Exists(resolvedPath))
        {
            throw new FileNotFoundException("Không tìm thấy ảnh tham chiếu nhân vật trong workspace.");
        }

        byte[] bytes;
        await using (var stream = new FileStream(resolvedPath, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            if (stream.Length != reference.SizeBytes || stream.Length > 10 * 1024 * 1024)
            {
                throw new InvalidDataException("Ảnh tham chiếu nhân vật đã thay đổi sau khi được duyệt.");
            }

            bytes = new byte[stream.Length];
            await stream.ReadExactlyAsync(bytes, cancellationToken);
        }

        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(hash),
                Encoding.ASCII.GetBytes(reference.Sha256.ToLowerInvariant())))
        {
            throw new InvalidDataException("Ảnh tham chiếu nhân vật không khớp bản đã được duyệt.");
        }

        return new VideoReferenceImageInput(
            reference.CharacterReferenceId,
            reference.MimeType,
            Convert.ToBase64String(bytes),
            hash);
    }

    private async Task<Guid> EnsureVideoGenerationAsync(
        SceneWorkItem scene,
        VideoTaskResponse task,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await dbContext.VideoGenerations.SingleOrDefaultAsync(
            x => x.ProviderRequestId == task.ProviderRequestId,
            cancellationToken);
        var localScene = await dbContext.Scenes.SingleAsync(x => x.SceneId == scene.SceneId, cancellationToken);
        var localPrompt = await dbContext.ScenePrompts.SingleAsync(x => x.ScenePromptId == scene.Prompt!.ScenePromptId, cancellationToken);
        if (existing is null)
        {
            var attempt = (await dbContext.VideoGenerations
                .Where(x => x.SceneId == scene.SceneId)
                .MaxAsync(x => (int?)x.AttemptNumber, cancellationToken) ?? 0) + 1;
            existing = new VideoGeneration
            {
                VideoGenerationId = Guid.NewGuid(),
                SceneId = scene.SceneId,
                ScenePromptId = localPrompt.ScenePromptId,
                ProviderRequestId = task.ProviderRequestId,
                AttemptNumber = attempt,
                Status = LocalGenerationStatus(task.Status),
                RequestedDurationMs = scene.GenerationDurationMs,
                CreatedAtUtc = DateTime.UtcNow
            };
            dbContext.VideoGenerations.Add(existing);
        }

        localScene.Status = LocalSceneStatus(task.Status);
        localScene.LastErrorCode = string.IsNullOrWhiteSpace(task.ErrorCode) ? null : SafeCode(task.ErrorCode);
        localScene.LastErrorMessage = string.IsNullOrWhiteSpace(task.ErrorMessage) ? null : SafeMessage(task.ErrorMessage);
        localScene.UpdatedAtUtc = DateTime.UtcNow;
        localPrompt.ProviderCode = task.ProviderCode;
        localPrompt.ModelCode = task.ModelCode;
        await dbContext.SaveChangesAsync(cancellationToken);
        return existing.VideoGenerationId;
    }

    private async Task UpdateGenerationWaitingStatusAsync(
        Guid generationId,
        Guid sceneId,
        string taskStatus,
        string? errorCode,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var generation = await dbContext.VideoGenerations.SingleAsync(
            x => x.VideoGenerationId == generationId,
            cancellationToken);
        var scene = await dbContext.Scenes.SingleAsync(x => x.SceneId == sceneId, cancellationToken);
        if (taskStatus == "Completed")
        {
            generation.Status = "Generated";
            scene.Status = "Generated";
        }
        else if (taskStatus is "Failed" or "Cancelled" or "Expired")
        {
            generation.Status = taskStatus == "Expired" ? "Failed" : taskStatus;
            scene.Status = taskStatus == "Expired" ? "Failed" : taskStatus;
        }
        else
        {
            generation.Status = "WaitingProvider";
            scene.Status = "WaitingProvider";
        }

        scene.LastErrorCode = string.IsNullOrWhiteSpace(errorCode) ? null : SafeCode(errorCode);
        scene.LastErrorMessage = string.IsNullOrWhiteSpace(errorMessage) ? null : SafeMessage(errorMessage);
        scene.UpdatedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task DownloadAndApproveAsync(
        Guid projectId,
        string remoteUserId,
        SceneWorkItem scene,
        Guid generationId,
        VideoTaskResponse task,
        CancellationToken cancellationToken)
    {
        string projectWorkspace;
        string aspectRatio;
        await using (var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken))
        {
            var project = await RequireProjectAsync(dbContext, projectId, remoteUserId, cancellationToken);
            projectWorkspace = project.WorkspaceRelativePath;
            aspectRatio = project.AspectRatio;
            var generation = await dbContext.VideoGenerations.SingleAsync(x => x.VideoGenerationId == generationId, cancellationToken);
            generation.Status = "Downloading";
            var localScene = await dbContext.Scenes.SingleAsync(x => x.SceneId == scene.SceneId, cancellationToken);
            localScene.Status = "Generated";
            localScene.LastErrorCode = null;
            localScene.LastErrorMessage = null;
            localScene.UpdatedAtUtc = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var fileName = $"scene-{scene.SequenceNumber:000}-{task.ProviderRequestId:N}.mp4";
        var assetRelativePath = Path.Combine("scenes", fileName).Replace(Path.DirectorySeparatorChar, '/');
        var workspaceRelativePath = Path.Combine(projectWorkspace, "scenes", fileName);
        var finalPath = workspaceService.Resolve(workspaceRelativePath);
        var partialPath = finalPath + ".part";
        var trimmedPartialPath = finalPath + ".trimmed.part";
        Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
        MediaProbeResult probe;
        AudioQualityResult? nativeAudioQuality;
        var trimmedToContentDuration = scene.ContentDurationMs > 0 &&
                                       scene.ContentDurationMs < scene.GenerationDurationMs;
        var outputAudioEnabled = !ReadBooleanProperty(
            scene.RequiredCapabilitiesJson,
            "muteOutputAudio");
        try
        {
            await apiClient.DownloadVideoAsync(task.OutputUrl!, partialPath, cancellationToken);
            probe = await mediaProbe.ProbeAsync(partialPath, cancellationToken);
            if (!probe.HasVideo)
            {
                throw new InvalidDataException("Tệp provider tải về không chứa luồng video.");
            }

            if (trimmedToContentDuration)
            {
                if (scene.ContentDurationMs % 1000 != 0)
                {
                    throw new InvalidDataException("Thời lượng video ngắn cần cắt phải là số giây nguyên.");
                }
                var targetDurationSeconds = checked((int)(scene.ContentDurationMs / 1000));
                await sceneVideoTrimmer.TrimAsync(
                    partialPath,
                    trimmedPartialPath,
                    targetDurationSeconds,
                    cancellationToken,
                    includeAudio: outputAudioEnabled);
                probe = await mediaProbe.ProbeAsync(trimmedPartialPath, cancellationToken);
                if (!probe.HasVideo ||
                    probe.DurationSeconds < targetDurationSeconds - 0.25m ||
                    probe.DurationSeconds > targetDurationSeconds + 0.5m)
                {
                    throw new InvalidDataException("Clip sau khi cắt không đạt đúng thời lượng người dùng đã chọn.");
                }
                if (!outputAudioEnabled && probe.HasAudio)
                {
                    throw new InvalidDataException("Clip đã tắt âm thanh nhưng file sau xử lý vẫn còn audio stream.");
                }
                nativeAudioQuality = outputAudioEnabled
                    ? await audioQualityValidator.AnalyzeAsync(trimmedPartialPath, cancellationToken)
                    : null;
                File.Move(trimmedPartialPath, finalPath, true);
                File.Delete(partialPath);
            }
            else if (!outputAudioEnabled)
            {
                await sceneVideoTrimmer.StripAudioAsync(
                    partialPath,
                    trimmedPartialPath,
                    cancellationToken);
                probe = await mediaProbe.ProbeAsync(trimmedPartialPath, cancellationToken);
                if (!probe.HasVideo || probe.HasAudio)
                {
                    throw new InvalidDataException("Clip tắt âm thanh sau xử lý không hợp lệ.");
                }
                nativeAudioQuality = null;
                File.Move(trimmedPartialPath, finalPath, true);
                File.Delete(partialPath);
            }
            else
            {
                nativeAudioQuality = await audioQualityValidator.AnalyzeAsync(partialPath, cancellationToken);
                File.Move(partialPath, finalPath, true);
            }
        }
        catch
        {
            if (File.Exists(partialPath))
            {
                File.Delete(partialPath);
            }
            if (File.Exists(trimmedPartialPath))
            {
                File.Delete(trimmedPartialPath);
            }
            throw;
        }

        string hash;
        await using (var stream = new FileStream(finalPath, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
        }

        var fileInfo = new FileInfo(finalPath);
        var speechExpected = !string.IsNullOrWhiteSpace(scene.SpokenText);
        var (expectedWidth, expectedHeight) = Dimensions(aspectRatio, "720p");
        var durationMs = probe.DurationSeconds > 0
            ? checked((long)Math.Round(probe.DurationSeconds * 1000m, MidpointRounding.AwayFromZero))
            : scene.GenerationDurationMs;
        await using var writeContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await RequireProjectAsync(writeContext, projectId, remoteUserId, cancellationToken);
        var generationToApprove = await writeContext.VideoGenerations.SingleAsync(
            x => x.VideoGenerationId == generationId,
            cancellationToken);
        var sceneToApprove = await writeContext.Scenes.SingleAsync(x => x.SceneId == scene.SceneId, cancellationToken);
        var asset = await writeContext.MediaAssets.SingleOrDefaultAsync(
            x => x.ProjectId == projectId && x.RelativePath == assetRelativePath,
            cancellationToken);
        if (asset is null)
        {
            asset = new MediaAsset
            {
                MediaAssetId = Guid.NewGuid(),
                ProjectId = projectId,
                SceneId = scene.SceneId,
                AssetType = "SceneVideo",
                DisplayName = $"Cảnh {scene.SequenceNumber}",
                RelativePath = assetRelativePath,
                MimeType = "video/mp4",
                SizeBytes = fileInfo.Length,
                Sha256 = hash,
                Width = probe.Width ?? expectedWidth,
                Height = probe.Height ?? expectedHeight,
                FrameRate = probe.FramesPerSecond,
                DurationMs = durationMs,
                AudioSampleRate = probe.AudioSampleRate,
                Status = "Ready",
                SourceType = "Generated",
                SourceProviderCode = task.ProviderCode,
                SourceExternalRequestId = task.ExternalRequestId,
                SourceProviderRequestId = task.ProviderRequestId,
                MetadataJson = JsonSerializer.Serialize(new
                {
                    task.ProviderRequestId,
                    task.ModelCode,
                    audioStrategy = outputAudioEnabled ? "ProviderNative" : "SilentOutput",
                    requestedContentDurationMs = scene.ContentDurationMs,
                    providerGenerationDurationMs = scene.GenerationDurationMs,
                    trimmedToContentDuration,
                    providerNativeAudioRequested = true,
                    outputAudioEnabled,
                    speechExpected,
                    speechMode = !string.IsNullOrWhiteSpace(scene.Dialogue)
                        ? KlingSpeechModes.OnCameraDialogue
                        : !string.IsNullOrWhiteSpace(scene.Narration)
                            ? KlingSpeechModes.NativeVoiceOver
                            : KlingSpeechModes.None,
                    spokenTextHash = Sha256Hex(scene.SpokenText ?? string.Empty),
                    nativeAudioExpected = outputAudioEnabled,
                    nativeAudioPresent = probe.HasAudio,
                    nativeAudioAudible = nativeAudioQuality?.IsAudible ?? false,
                    meanVolumeDb = nativeAudioQuality?.MeanVolumeDb,
                    maxVolumeDb = nativeAudioQuality?.MaxVolumeDb,
                    silentRatio = nativeAudioQuality?.SilentRatio,
                    probe.VideoCodec,
                    probe.AudioCodec,
                    warningCode = nativeAudioQuality?.FailureCode
                }, JsonOptions),
                CreatedAtUtc = DateTime.UtcNow,
                VerifiedAtUtc = DateTime.UtcNow
            };
            writeContext.MediaAssets.Add(asset);
        }

        generationToApprove.OutputMediaAssetId = asset.MediaAssetId;
        generationToApprove.ActualDurationMs = durationMs;
        generationToApprove.QualityReportJson = JsonSerializer.Serialize(new
        {
            audioStrategy = outputAudioEnabled ? "ProviderNative" : "SilentOutput",
            requestedContentDurationMs = scene.ContentDurationMs,
            providerGenerationDurationMs = scene.GenerationDurationMs,
            trimmedToContentDuration,
            providerNativeAudioRequested = true,
            outputAudioEnabled,
            speechExpected,
            nativeAudioExpected = outputAudioEnabled,
            nativeAudioPresent = probe.HasAudio,
            nativeAudioAudible = nativeAudioQuality?.IsAudible ?? false,
            meanVolumeDb = nativeAudioQuality?.MeanVolumeDb,
            maxVolumeDb = nativeAudioQuality?.MaxVolumeDb,
            silentRatio = nativeAudioQuality?.SilentRatio,
            issues = !outputAudioEnabled || nativeAudioQuality?.IsAudible == true
                ? Array.Empty<string>()
                : new[] { nativeAudioQuality?.FailureCode ?? "native_audio_invalid" }
        }, JsonOptions);
        var nativeAudioInvalid = outputAudioEnabled &&
                                 (!probe.HasAudio || nativeAudioQuality?.IsAudible != true);
        generationToApprove.Status = outputAudioEnabled
            ? nativeAudioInvalid ? "NativeAudioInvalid" : "AudioReviewRequired"
            : "Approved";
        generationToApprove.CompletedAtUtc = DateTime.UtcNow;
        sceneToApprove.ApprovedGenerationId = outputAudioEnabled
            ? null
            : generationToApprove.VideoGenerationId;
        sceneToApprove.Status = outputAudioEnabled
            ? nativeAudioInvalid ? "NativeAudioInvalid" : "AudioReviewRequired"
            : "Approved";
        sceneToApprove.LastErrorCode = nativeAudioInvalid
            ? nativeAudioQuality?.FailureCode ?? "provider_native_audio_inaudible"
            : null;
        sceneToApprove.LastErrorMessage = nativeAudioInvalid
            ? nativeAudioQuality?.FailureMessage ?? "Clip provider không có Native Audio nghe được. Hãy tạo lại cảnh."
            : null;
        sceneToApprove.UpdatedAtUtc = DateTime.UtcNow;
        await writeContext.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureSceneNarrationAsync(
        Guid projectId,
        string remoteUserId,
        SceneWorkItem scene,
        CancellationToken cancellationToken)
    {
        var narration = NormalizeNarration(scene.Narration);
        if (string.IsNullOrWhiteSpace(narration))
        {
            return;
        }
        var narrationHash = Sha256Hex(narration);
        string projectWorkspace;
        string voiceCode;
        decimal speakingRate;
        Guid? existingVoiceAssetId;
        Guid? existingVoiceRequestId;
        string? existingVoiceRelativePath;
        await using (var readContext = await dbContextFactory.CreateDbContextAsync(cancellationToken))
        {
            var project = await RequireProjectAsync(readContext, projectId, remoteUserId, cancellationToken);
            projectWorkspace = project.WorkspaceRelativePath;
            voiceCode = project.VoiceCode?.Trim()
                ?? throw new InvalidOperationException("Hãy chọn giọng đọc cho dự án trước khi tạo video.");
            speakingRate = project.VoiceSpeakingRate ?? 1m;
            var existingVoice = await readContext.VoiceGenerations
                .AsNoTracking()
                .Where(x => x.SceneId == scene.SceneId &&
                            x.ScenePlanVersion == scene.ScenePlanVersion &&
                            x.NarrationHash == narrationHash &&
                            x.VoiceCode == voiceCode &&
                            x.SpeakingRate == speakingRate &&
                            x.Status == "Completed" &&
                            x.OutputMediaAssetId != null &&
                            x.OutputMediaAsset!.Status == "Ready" &&
                            x.OutputMediaAsset.DeletedAtUtc == null)
                .OrderByDescending(x => x.Version)
                .Select(x => new
                {
                    x.ProviderRequestId,
                    AssetId = x.OutputMediaAssetId,
                    x.OutputMediaAsset!.RelativePath
                })
                .FirstOrDefaultAsync(cancellationToken);
            existingVoiceAssetId = existingVoice?.AssetId;
            existingVoiceRequestId = existingVoice?.ProviderRequestId;
            existingVoiceRelativePath = existingVoice?.RelativePath;
        }

        string voicePath;
        Guid voiceAssetId;
        Guid voiceProviderRequestId;
        if (existingVoiceAssetId is { } readyVoiceAssetId &&
            existingVoiceRequestId is { } readyVoiceRequestId &&
            !string.IsNullOrWhiteSpace(existingVoiceRelativePath))
        {
            var relative = Path.Combine(
                projectWorkspace.Replace('/', Path.DirectorySeparatorChar),
                existingVoiceRelativePath.Replace('/', Path.DirectorySeparatorChar));
            voicePath = workspaceService.Resolve(relative);
            if (!File.Exists(voicePath))
            {
                throw new FileNotFoundException("Metadata giọng đọc đã có nhưng file trong workspace bị thiếu.", voicePath);
            }
            voiceAssetId = readyVoiceAssetId;
            voiceProviderRequestId = readyVoiceRequestId;
        }
        else
        {
            var voiceSnapshotHash = Sha256Hex($"{voiceCode}\n{speakingRate.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            var response = await apiClient.GenerateSceneVoiceAsync(
                new GenerateSceneVoiceRequest(
                    projectId,
                    scene.SceneId,
                    scene.ScenePlanVersion,
                    narrationHash,
                    $"scene-voice:{scene.SceneId:N}:v{scene.ScenePlanVersion}:{narrationHash}:{voiceSnapshotHash}"),
                cancellationToken);
            if (response.Status != "Completed" ||
                !string.Equals(response.VoiceCode, voiceCode, StringComparison.Ordinal) ||
                response.DurationMs <= 0)
            {
                throw new InvalidDataException("Server trả về kết quả giọng đọc không hợp lệ.");
            }

            var voiceFileName = $"scene-{scene.SequenceNumber:000}-{response.ProviderRequestId:N}.wav";
            var voiceAssetRelativePath = Path.Combine("voice", voiceFileName).Replace(Path.DirectorySeparatorChar, '/');
            var voiceWorkspacePath = Path.Combine(projectWorkspace, "voice", voiceFileName);
            var finalVoicePath = workspaceService.Resolve(voiceWorkspacePath);
            var partialVoicePath = finalVoicePath + ".part";
            Directory.CreateDirectory(Path.GetDirectoryName(finalVoicePath)!);
            if (File.Exists(partialVoicePath))
            {
                File.Delete(partialVoicePath);
            }
            try
            {
                await apiClient.DownloadSceneVoiceAsync(response, partialVoicePath, cancellationToken);
                var voiceProbe = await mediaProbe.ProbeAsync(partialVoicePath, cancellationToken);
                await audioQualityValidator.RequireAudibleAsync(
                    partialVoicePath,
                    "Giọng đọc tải về không nghe được",
                    cancellationToken);
                if (!voiceProbe.HasAudio || voiceProbe.DurationSeconds <= 0 ||
                    voiceProbe.AudioSampleRate != response.SampleRate)
                {
                    throw new InvalidDataException("Giọng đọc tải về không khớp thông số âm thanh đã xác nhận.");
                }
                File.Move(partialVoicePath, finalVoicePath, true);
            }
            catch
            {
                if (File.Exists(partialVoicePath))
                {
                    File.Delete(partialVoicePath);
                }
                throw;
            }

            await using var writeContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            await using var transaction = await writeContext.Database.BeginTransactionAsync(cancellationToken);
            await RequireProjectAsync(writeContext, projectId, remoteUserId, cancellationToken);
            var voiceGeneration = await writeContext.VoiceGenerations.SingleOrDefaultAsync(
                x => x.ProviderRequestId == response.ProviderRequestId &&
                     x.SceneId == scene.SceneId &&
                     x.ProjectId == projectId,
                cancellationToken)
                ?? throw new InvalidDataException("Không tìm thấy bản ghi VoiceGeneration do server vừa tạo.");
            var voiceAsset = await writeContext.MediaAssets.SingleOrDefaultAsync(
                x => x.SourceProviderRequestId == response.ProviderRequestId,
                cancellationToken);
            if (voiceAsset is null)
            {
                voiceAsset = new MediaAsset
                {
                    MediaAssetId = Guid.NewGuid(),
                    ProjectId = projectId,
                    SceneId = scene.SceneId,
                    AssetType = "SceneVoice",
                    DisplayName = $"Giọng đọc cảnh {scene.SequenceNumber}",
                    RelativePath = voiceAssetRelativePath,
                    MimeType = response.MimeType,
                    SizeBytes = response.SizeBytes,
                    Sha256 = response.Sha256,
                    DurationMs = response.DurationMs,
                    AudioSampleRate = response.SampleRate,
                    Status = "Ready",
                    SourceType = "Generated",
                    SourceProviderCode = response.ProviderCode,
                    SourceExternalRequestId = response.ProviderRequestId.ToString("D"),
                    SourceProviderRequestId = response.ProviderRequestId,
                    MetadataJson = JsonSerializer.Serialize(new
                    {
                        response.ModelCode,
                        response.VoiceCode,
                        response.ProviderVoiceCode,
                        response.Channels,
                        narrationHash,
                        scene.ScenePlanVersion
                    }, JsonOptions),
                    CreatedAtUtc = DateTime.UtcNow,
                    VerifiedAtUtc = DateTime.UtcNow,
                    RowVersion = new byte[8]
                };
                writeContext.MediaAssets.Add(voiceAsset);
            }
            voiceGeneration.OutputMediaAssetId = voiceAsset.MediaAssetId;
            voiceGeneration.Status = "Completed";
            voiceGeneration.DurationMs = response.DurationMs;
            voiceGeneration.CompletedAtUtc ??= DateTime.UtcNow;
            await writeContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            voicePath = finalVoicePath;
            voiceAssetId = voiceAsset.MediaAssetId;
            voiceProviderRequestId = response.ProviderRequestId;
        }

        string rawVideoPath;
        Guid rawVideoAssetId;
        await using (var readContext = await dbContextFactory.CreateDbContextAsync(cancellationToken))
        {
            var rawAsset = await readContext.Scenes
                .AsNoTracking()
                .Where(x => x.SceneId == scene.SceneId && x.ProjectId == projectId && x.ApprovedGenerationId != null)
                .Select(x => new
                {
                    AssetId = x.ApprovedGeneration!.OutputMediaAssetId,
                    x.ApprovedGeneration!.OutputMediaAsset!.RelativePath
                })
                .SingleOrDefaultAsync(cancellationToken)
                ?? throw new InvalidDataException("Cảnh chưa có clip video đã duyệt để ghép giọng đọc.");
            if (rawAsset.AssetId is null || string.IsNullOrWhiteSpace(rawAsset.RelativePath))
            {
                throw new InvalidDataException("Clip video đã duyệt chưa có MediaAsset hợp lệ.");
            }
            rawVideoAssetId = rawAsset.AssetId.Value;
            rawVideoPath = workspaceService.Resolve(Path.Combine(
                projectWorkspace.Replace('/', Path.DirectorySeparatorChar),
                rawAsset.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
        }
        if (!File.Exists(rawVideoPath))
        {
            throw new FileNotFoundException("Không tìm thấy clip video trong workspace.", rawVideoPath);
        }

        var narratedFileName = $"scene-{scene.SequenceNumber:000}-narrated-{voiceProviderRequestId:N}.mp4";
        var narratedAssetRelativePath = Path.Combine("scenes", narratedFileName).Replace(Path.DirectorySeparatorChar, '/');
        var narratedPath = workspaceService.Resolve(Path.Combine(projectWorkspace, "scenes", narratedFileName));
        await using (var existingContext = await dbContextFactory.CreateDbContextAsync(cancellationToken))
        {
            var exists = await existingContext.MediaAssets.AsNoTracking().AnyAsync(
                x => x.ProjectId == projectId &&
                     x.SceneId == scene.SceneId &&
                     x.AssetType == "SceneVideoNarrated" &&
                     x.RelativePath == narratedAssetRelativePath &&
                     x.Status == "Ready" &&
                     x.DeletedAtUtc == null,
                cancellationToken);
            if (exists && File.Exists(narratedPath))
            {
                return;
            }
        }

        var narratedPartialPath = narratedPath + ".part";
        if (File.Exists(narratedPartialPath))
        {
            File.Delete(narratedPartialPath);
        }
        SceneAudioMixResult mixResult;
        try
        {
            mixResult = await sceneAudioMixer.MixAsync(
                rawVideoPath,
                voicePath,
                narratedPartialPath,
                scene.GenerationDurationMs / 1000m,
                cancellationToken);
            File.Move(narratedPartialPath, narratedPath, true);
        }
        catch
        {
            if (File.Exists(narratedPartialPath))
            {
                File.Delete(narratedPartialPath);
            }
            throw;
        }

        string narratedHash;
        await using (var stream = new FileStream(
            narratedPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            narratedHash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
        }
        var narratedFile = new FileInfo(narratedPath);
        await using (var writeContext = await dbContextFactory.CreateDbContextAsync(cancellationToken))
        {
            await RequireProjectAsync(writeContext, projectId, remoteUserId, cancellationToken);
            var narratedAsset = await writeContext.MediaAssets.SingleOrDefaultAsync(
                x => x.ProjectId == projectId && x.RelativePath == narratedAssetRelativePath,
                cancellationToken);
            if (narratedAsset is null)
            {
                narratedAsset = new MediaAsset
                {
                    MediaAssetId = Guid.NewGuid(),
                    ProjectId = projectId,
                    SceneId = scene.SceneId,
                    AssetType = "SceneVideoNarrated",
                    DisplayName = $"Cảnh {scene.SequenceNumber} có lời đọc",
                    RelativePath = narratedAssetRelativePath,
                    MimeType = "video/mp4",
                    SizeBytes = narratedFile.Length,
                    Sha256 = narratedHash,
                    Width = mixResult.OutputProbe.Width,
                    Height = mixResult.OutputProbe.Height,
                    FrameRate = mixResult.OutputProbe.FramesPerSecond,
                    DurationMs = checked((long)Math.Round(mixResult.OutputProbe.DurationSeconds * 1000m, MidpointRounding.AwayFromZero)),
                    AudioSampleRate = mixResult.OutputProbe.AudioSampleRate,
                    Status = "Ready",
                    SourceType = "Generated",
                    SourceProviderCode = "local-ffmpeg",
                    MetadataJson = JsonSerializer.Serialize(new
                    {
                        RawVideoMediaAssetId = rawVideoAssetId,
                        VoiceMediaAssetId = voiceAssetId,
                        VoiceProviderRequestId = voiceProviderRequestId,
                        mixResult.PreservedNativeAudio,
                        mixResult.OutputAudioQuality.MeanVolumeDb,
                        mixResult.OutputAudioQuality.MaxVolumeDb,
                        mixResult.OutputAudioQuality.SilentRatio
                    }, JsonOptions),
                    CreatedAtUtc = DateTime.UtcNow,
                    VerifiedAtUtc = DateTime.UtcNow,
                    RowVersion = new byte[8]
                };
                writeContext.MediaAssets.Add(narratedAsset);
            }
            var localScene = await writeContext.Scenes.SingleAsync(x => x.SceneId == scene.SceneId, cancellationToken);
            localScene.Status = "Approved";
            localScene.LastErrorCode = null;
            localScene.LastErrorMessage = null;
            localScene.UpdatedAtUtc = DateTime.UtcNow;
            await writeContext.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<bool> IsSceneApprovedAsync(Guid sceneId, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.Scenes.AsNoTracking().AnyAsync(
            x => x.SceneId == sceneId && x.Status == "Approved" && x.ApprovedGenerationId != null,
            cancellationToken);
    }

    private async Task<bool> AreAllScenesApprovedAsync(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var planVersion = await dbContext.Projects
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .Select(x => x.CurrentScenePlanVersion)
            .SingleAsync(cancellationToken);
        return planVersion.HasValue &&
               await dbContext.Scenes
                   .AsNoTracking()
                   .Where(x => x.ProjectId == projectId && x.ScenePlanVersion == planVersion.Value)
                   .AllAsync(
                       x => x.ApprovedGenerationId != null,
                       cancellationToken);
    }

    private async Task UpdateProjectStatusAsync(
        Guid projectId,
        string remoteUserId,
        string status,
        string? errorCode,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var project = await RequireProjectAsync(dbContext, projectId, remoteUserId, cancellationToken);
        project.Status = status;
        project.LastErrorCode = errorCode;
        project.LastErrorMessage = errorMessage;
        project.UpdatedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task MarkSceneSpeechValidationFailedAsync(
        Guid projectId,
        string remoteUserId,
        Guid sceneId,
        AccountClientException exception,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var project = await RequireProjectAsync(dbContext, projectId, remoteUserId, cancellationToken);
        var scene = await dbContext.Scenes.SingleOrDefaultAsync(
            x => x.SceneId == sceneId && x.ProjectId == projectId,
            cancellationToken);
        if (scene is null)
        {
            return;
        }

        var now = DateTime.UtcNow;
        scene.Status = "PromptInvalid";
        scene.LastErrorCode = SafeCode(exception.Code);
        scene.LastErrorMessage = SafeMessage(exception.Message);
        scene.UpdatedAtUtc = now;
        project.Status = "ScenePlanning";
        project.LastErrorCode = SafeCode(exception.Code);
        project.LastErrorMessage = SafeMessage(exception.Message);
        project.UpdatedAtUtc = now;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task MarkMediaToolBlockedAsync(
        Guid projectId,
        string remoteUserId,
        IReadOnlyCollection<Guid>? requestedSceneIds,
        MediaToolUnavailableException exception,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var project = await RequireProjectAsync(dbContext, projectId, remoteUserId, cancellationToken);
        var requested = requestedSceneIds?.ToArray();
        var generations = await dbContext.VideoGenerations
            .Include(x => x.Scene)
            .Include(x => x.ProviderRequest)
            .Where(x =>
                x.Scene.ProjectId == projectId &&
                x.ProviderRequest.Status == "Completed" &&
                x.OutputMediaAssetId == null &&
                (x.Status == "Downloading" || x.Status == "Generated") &&
                (requested == null || requested.Contains(x.SceneId)))
            .ToListAsync(cancellationToken);
        foreach (var generation in generations)
        {
            generation.Status = "Generated";
            generation.Scene.Status = "Generated";
            generation.Scene.LastErrorCode = SafeCode(exception.Code);
            generation.Scene.LastErrorMessage = SafeMessage(exception.Message);
            generation.Scene.UpdatedAtUtc = DateTime.UtcNow;
        }

        project.Status = "ScenePlanning";
        project.LastErrorCode = SafeCode(exception.Code);
        project.LastErrorMessage = SafeMessage(exception.Message);
        project.UpdatedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task<Project> RequireProjectAsync(
        VideoFactoryDbContext dbContext,
        Guid projectId,
        string remoteUserId,
        CancellationToken cancellationToken) =>
        await dbContext.Projects.SingleOrDefaultAsync(
            x => x.ProjectId == projectId && x.RemoteUserId == remoteUserId && x.DeletedAtUtc == null,
            cancellationToken)
        ?? throw new ArgumentException("Không tìm thấy dự án của tài khoản hiện tại.");

    private static void ValidateContentPlan(GeneratedContentPlan plan)
    {
        if (plan.Scenes.Count == 0 || plan.Scenes.Any(x => x.DurationSeconds is < 3 or > 30))
        {
            throw new InvalidDataException("Content plan OpenAI không có danh sách cảnh hợp lệ.");
        }

        if (plan.Scenes.Select(x => x.SequenceNumber).Distinct().Count() != plan.Scenes.Count)
        {
            throw new InvalidDataException("Content plan OpenAI có số thứ tự cảnh bị trùng.");
        }

        var characterKeys = plan.Characters
            .Select(x => x.CharacterKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (characterKeys.Count != plan.Characters.Count ||
            plan.Characters.Any(character =>
                string.IsNullOrWhiteSpace(character.CharacterKey) ||
                string.IsNullOrWhiteSpace(character.Name) ||
                string.IsNullOrWhiteSpace(character.VisualIdentity)) ||
            plan.Scenes.Any(scene => scene.CharacterKeys.Any(key => !characterKeys.Contains(key))))
        {
            throw new InvalidDataException("Content plan OpenAI có liên kết nhân vật không hợp lệ.");
        }

        foreach (var scene in plan.Scenes)
        {
            var mode = ResolveSpeechMode(scene);
            var spokenText = NormalizeNarration(scene.Narration);
            var speaker = NullIfWhiteSpace(scene.SpeakerCharacterKey);
            if (mode == KlingSpeechModes.None)
            {
                if (spokenText.Length > 0 || speaker is not null)
                {
                    throw new InvalidDataException("Cảnh không lời không được chứa nội dung hoặc người nói.");
                }
                continue;
            }

            if (spokenText.Length == 0)
            {
                throw new InvalidDataException("Cảnh có lời provider nhưng nội dung lời đang trống.");
            }
            var wordCount = NativeSpeechWordBudget.CountWords(spokenText);
            var maximumWords = NativeSpeechWordBudget.MaximumWordsForDurationSeconds(scene.DurationSeconds);
            if (wordCount > maximumWords)
            {
                throw new InvalidDataException(
                    $"Lời provider ở cảnh {scene.SequenceNumber} có {wordCount} từ, vượt mức {maximumWords} từ cho clip {scene.DurationSeconds} giây.");
            }
            if (mode == KlingSpeechModes.OnCameraDialogue &&
                (scene.CharacterKeys.Count != 1 ||
                 speaker is null ||
                 !string.Equals(speaker, scene.CharacterKeys[0], StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidDataException("Cảnh thoại trực tiếp phải gắn đúng một nhân vật nói.");
            }
            if (mode == KlingSpeechModes.NativeVoiceOver && speaker is not null)
            {
                throw new InvalidDataException("Cảnh voice-over không được gắn nhân vật nói trực tiếp.");
            }
        }
    }

    private static string ResolveSpeechMode(GeneratedContentScene scene)
    {
        if (scene.SpeechMode is KlingSpeechModes.None or
            KlingSpeechModes.OnCameraDialogue or
            KlingSpeechModes.NativeVoiceOver)
        {
            return scene.SpeechMode;
        }

        if (string.IsNullOrWhiteSpace(scene.Narration))
        {
            return KlingSpeechModes.None;
        }

        return scene.CharacterKeys.Count == 1
            ? KlingSpeechModes.OnCameraDialogue
            : KlingSpeechModes.NativeVoiceOver;
    }

    private static string ComposeKlingPrompt(string positive, string? negative)
    {
        var prompt = string.IsNullOrWhiteSpace(negative)
            ? positive.Trim()
            : $"{positive.Trim()} Avoid: {negative.Trim()}";
        return prompt.Length <= 3072 ? prompt : prompt[..3072];
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

    private static string LocalGenerationStatus(string taskStatus) => taskStatus switch
    {
        "Completed" => "Generated",
        "Failed" => "Failed",
        "Cancelled" => "Cancelled",
        "Expired" => "Failed",
        _ => "WaitingProvider"
    };

    private static string LocalSceneStatus(string taskStatus) => taskStatus switch
    {
        "Completed" => "Generated",
        "Failed" => "Failed",
        "Cancelled" => "Cancelled",
        "Expired" => "Failed",
        _ => "WaitingProvider"
    };

    private static string Sha256Hex(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string NormalizeNarration(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Trim();

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static (int Width, int Height) Dimensions(string aspectRatio, string resolution) =>
        (aspectRatio, resolution) switch
        {
            ("9:16", "720p") => (720, 1280),
            ("1:1", "720p") => (720, 720),
            _ => (1280, 720)
        };

    private static string SafeCode(string value) => value.Length <= 100 ? value : value[..100];

    private static string SafeMessage(string value) => value.Length <= 4000 ? value : value[..4000];

    private static bool IsSpeechWordBudgetError(string? code) =>
        code == "kling_spoken_text_too_long";

    private sealed record SceneWorkItem(
        Guid SceneId,
        Guid ScriptId,
        int ScenePlanVersion,
        int SequenceNumber,
        long ContentDurationMs,
        long GenerationDurationMs,
        string? Narration,
        string? Dialogue,
        string? RequiredCapabilitiesJson,
        string Status,
        string? CharacterIdsJson,
        PromptWorkItem? Prompt)
    {
        public string? SpokenText => !string.IsNullOrWhiteSpace(Dialogue) ? Dialogue : Narration;

        public CharacterWorkItem? Character { get; init; }
    }

    private sealed record PromptWorkItem(
        Guid ScenePromptId,
        int Version,
        string FinalPrompt,
        string? NegativePrompt);

    private sealed record CharacterWorkItem(
        Guid CharacterId,
        string Name,
        string Status,
        ReferenceWorkItem? Reference);

    private sealed record ReferenceWorkItem(
        Guid CharacterReferenceId,
        string RelativePath,
        string MimeType,
        string Sha256,
        long SizeBytes);
}
