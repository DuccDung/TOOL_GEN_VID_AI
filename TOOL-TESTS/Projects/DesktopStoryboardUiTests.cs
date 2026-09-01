namespace TOOL_TESTS.Projects;

public sealed class DesktopStoryboardUiTests
{
    [Fact]
    public void ProjectPreview_UsesOnlyCompletedFinalVideoAndNeverFallsBackToSceneClip()
    {
        var app = ReadRepositoryFile("TOOL-LOCAL", "Web", "src", "App.tsx");
        var service = ReadRepositoryFile("TOOL-LOCAL", "Projects", "ProjectService.cs");

        var queryStart = service.IndexOf("var finalVideoCandidates", StringComparison.Ordinal);
        var queryEnd = service.IndexOf("var characterRows", queryStart, StringComparison.Ordinal);
        Assert.True(queryStart >= 0 && queryEnd > queryStart);
        var finalPreviewQuery = service[queryStart..queryEnd];

        Assert.Contains("x.RenderJob.Status == \"Completed\"", finalPreviewQuery);
        Assert.Contains("x.MediaAsset.AssetType == \"FinalVideo\"", finalPreviewQuery);
        Assert.Contains("x.MediaAsset.Status == \"Ready\"", finalPreviewQuery);
        Assert.Contains("x.MediaAsset.DeletedAtUtc == null", finalPreviewQuery);
        Assert.DoesNotContain("AssetType == \"SceneVideo\"", finalPreviewQuery, StringComparison.Ordinal);
        Assert.Contains("FirstOrDefault(preview => preview is not null)", finalPreviewQuery);

        var previewStart = app.IndexOf("function getFinalPreviewState", StringComparison.Ordinal);
        var previewEnd = app.IndexOf("function ProjectInfoCard", previewStart, StringComparison.Ordinal);
        Assert.True(previewStart >= 0 && previewEnd > previewStart);
        var previewComponent = app[previewStart..previewEnd];

        Assert.Contains("Video hoàn chỉnh đã sẵn sàng", previewComponent);
        Assert.Contains("FFmpeg đang dựng video hoàn chỉnh", previewComponent);
        Assert.Contains("Đang xử lý clip cảnh", previewComponent);
        Assert.Contains("Các cảnh đã sẵn sàng · hãy dựng video cuối", previewComponent);
        Assert.Contains("key={preview.url}", previewComponent);
        Assert.DoesNotContain("scene.preview", previewComponent, StringComparison.Ordinal);
        Assert.Contains("return Boolean(project.preview?.url);", app);
    }

    [Fact]
    public void Storyboard_RendersRealSceneContentAndLocalVideoPreview()
    {
        var app = ReadRepositoryFile("TOOL-LOCAL", "Web", "src", "App.tsx");
        var types = ReadRepositoryFile("TOOL-LOCAL", "Web", "src", "types.ts");
        var service = ReadRepositoryFile("TOOL-LOCAL", "Projects", "ProjectService.cs");

        Assert.Contains("function StoryboardSection", app);
        Assert.Contains("Nội dung và hình ảnh từng cảnh", app);
        Assert.Contains("scene.narration", app);
        Assert.Contains("scene.visualDescription", app);
        Assert.Contains("scene.prompt", app);
        Assert.Contains("src={scene.preview.url}", app);
        Assert.Contains("scenes: SceneSummary[]", types);
        Assert.Contains("CreatePreview(", service);
        Assert.Contains("AssetType == \"SceneVideo\"", service);
    }

    [Fact]
    public void LongVideoContentStep_RendersReadOnlyDetailsForEveryGeneratedScene()
    {
        var app = ReadRepositoryFile("TOOL-LOCAL", "Web", "src", "App.tsx");
        var styles = ReadRepositoryFile("TOOL-LOCAL", "Web", "src", "styles.css");

        Assert.Contains("<LongVideoContentScenes scenes={project.scenes} />", app);
        Assert.Contains("function LongVideoContentScenes", app);
        Assert.Contains("CHI TIẾT KỊCH BẢN", app);
        Assert.Contains("Chưa có cảnh để hiển thị", app);
        Assert.Contains("scene.storyPurpose", app);
        Assert.Contains("scene.timelineStartMs", app);
        Assert.Contains("scene.narration", app);
        Assert.Contains("scene.visualDescription", app);
        Assert.Contains("scene.prompt", app);
        Assert.Contains("scene.voiceStyle", app);
        Assert.Contains("scene.ambientAudio", app);
        Assert.Contains("scene.soundEffects", app);
        Assert.Contains("Thu gọn tất cả", app);
        Assert.Contains(".long-video-content-scenes { overflow: hidden", styles);
        Assert.Contains(".content-scene-copy-grid { display: grid", styles);

        var componentStart = app.IndexOf("function LongVideoContentScenes", StringComparison.Ordinal);
        var componentEnd = app.IndexOf("function LongVideoExportOverview", componentStart, StringComparison.Ordinal);
        Assert.True(componentStart >= 0 && componentEnd > componentStart);
        var component = app[componentStart..componentEnd];
        Assert.DoesNotContain("postToHost(", component, StringComparison.Ordinal);
        Assert.DoesNotContain("onGenerateVideo", component, StringComparison.Ordinal);
        Assert.DoesNotContain("onUpdateScene", component, StringComparison.Ordinal);
    }

    [Fact]
    public void LongFormLanguagePolicy_IsVisibleForKlingAndFalAndDoesNotChangeShortVideoPromptFlow()
    {
        var app = ReadRepositoryFile("TOOL-LOCAL", "Web", "src", "App.tsx");
        var types = ReadRepositoryFile("TOOL-LOCAL", "Web", "src", "types.ts");
        var projectService = ReadRepositoryFile("TOOL-LOCAL", "Projects", "ProjectService.cs");

        Assert.Contains("requiresVietnameseContentRegeneration", types);
        Assert.Contains("Tiếng Việt (bắt buộc cho Video Dài dùng ${", app);
        Assert.Contains("['kling', 'fal']", app);
        Assert.Contains("Sinh lại nội dung tiếng Việt", app);
        Assert.Contains("Dự án Video Dài này còn nội dung tiếng Anh", app);
        Assert.Contains("workflowStructureType", projectService);
        Assert.Contains("KlingLongFormVietnameseValidator.RequiresVietnamese", projectService);

        var shortVideoStart = app.IndexOf("function ShortVideoPage", StringComparison.Ordinal);
        var shortVideoEnd = app.IndexOf("function LongVideoPage", shortVideoStart, StringComparison.Ordinal);
        Assert.True(shortVideoStart >= 0 && shortVideoEnd > shortVideoStart);
        var shortVideoComponent = app[shortVideoStart..shortVideoEnd];
        Assert.Contains("Nội dung được dùng trực tiếp làm prompt hình ảnh", shortVideoComponent);
        Assert.DoesNotContain("requiresVietnameseContentRegeneration", shortVideoComponent, StringComparison.Ordinal);
    }

    [Fact]
    public void AssetStep_ShowsAiGeneratedAssetsSceneChipsAndRetryWithoutProviderCall()
    {
        var app = ReadRepositoryFile("TOOL-LOCAL", "Web", "src", "App.tsx");
        var types = ReadRepositoryFile("TOOL-LOCAL", "Web", "src", "types.ts");
        var bridge = ReadRepositoryFile("TOOL-LOCAL", "WebView", "DashboardBridge.cs");

        Assert.Contains("postToHost('project-asset.materialize')", app);
        Assert.Contains("Khôi phục đề xuất AI", app);
        Assert.Contains("AI đề xuất", app);
        Assert.Contains("Cảnh {sequence}", app);
        Assert.Contains("sourceKind: 'Manual' | 'AiGenerated'", types);
        Assert.Contains("SynchronizeProjectAssetPlanAsync", bridge);
        Assert.Contains("Đang đồng bộ thư viện tài sản từ content plan đã lưu", bridge);
    }

    [Fact]
    public void NativeAudioApproval_RequiresPreviewPlaybackInUiAndDesktopService()
    {
        var app = ReadRepositoryFile("TOOL-LOCAL", "Web", "src", "App.tsx");
        var bridgeContracts = ReadRepositoryFile("TOOL-LOCAL", "WebView", "WebMessageContracts.cs");
        var service = ReadRepositoryFile("TOOL-LOCAL", "Projects", "ProjectService.cs");

        Assert.Contains("onPlay={() => setPreviewPlaybackConfirmed(true)}", app);
        Assert.Contains("!previewPlaybackConfirmed", app);
        Assert.Contains("{ sceneId, playbackConfirmed }", app);
        Assert.Contains("bool PlaybackConfirmed", bridgeContracts);
        Assert.Contains("if (!playbackConfirmed)", service);
    }

    [Fact]
    public void KlingLongFormSpeechUi_BlocksVoiceOverWithCharacterAndExplainsRecoveryRetry()
    {
        var app = ReadRepositoryFile("TOOL-LOCAL", "Web", "src", "App.tsx");
        var projectService = ReadRepositoryFile("TOOL-LOCAL", "Projects", "ProjectService.cs");

        Assert.Contains("enforceKlingLongFormSpeechPolicy", app);
        Assert.Contains("Lời dẫn ngoài khung hình chỉ dùng cho cảnh B-roll không có nhân vật", app);
        Assert.Contains("Tạo lại với prompt ưu tiên lời thoại", app);
        Assert.Contains("phát sinh chi phí provider mới", app);
        Assert.Contains("Tôi đã nghe rõ đủ câu và đúng nguyên văn", app);
        Assert.Contains("Đúng nhân vật trên màn hình đang nói", app);
        Assert.Contains("Khẩu hình và biểu cảm chấp nhận được", app);
        Assert.Contains("KlingLongFormSpeechIntentValidator.FindViolation", projectService);

        var shortVideoStart = app.IndexOf("function ShortVideoPage", StringComparison.Ordinal);
        var shortVideoEnd = app.IndexOf("function LongVideoPage", shortVideoStart, StringComparison.Ordinal);
        Assert.True(shortVideoStart >= 0 && shortVideoEnd > shortVideoStart);
        Assert.DoesNotContain("enforceKlingLongFormSpeechPolicy", app[shortVideoStart..shortVideoEnd], StringComparison.Ordinal);
    }

    [Fact]
    public void StoryboardPreview_StaysInsideItsGridColumnAndNarrationIsReadable()
    {
        var app = ReadRepositoryFile("TOOL-LOCAL", "Web", "src", "App.tsx");
        var styles = ReadRepositoryFile("TOOL-LOCAL", "Web", "src", "styles.css");

        Assert.DoesNotContain("style={{ aspectRatio: ratio }}", app, StringComparison.Ordinal);
        Assert.Contains("<header className=\"scene-heading\">", app);
        Assert.Contains("<div className=\"scene-card-body\">", app);
        Assert.Contains("<footer className=\"scene-footer\">", app);
        Assert.Contains("Chưa có thumbnail", app);
        Assert.Contains("function ExpandableSceneText", app);
        Assert.Contains("function sceneDisplayTitle", app);
        Assert.Contains("spellCheck={false}", app);
        Assert.Contains(".scene-card { display: block", styles);
        Assert.Contains(".scene-card-body { display: grid; grid-template-columns: minmax(250px,31%) minmax(0,1fr)", styles);
        Assert.Contains("min-width: 0; max-width: 100%; min-height: 0", styles);
        Assert.Contains("aspect-ratio: 16 / 9", styles);
        Assert.Contains("white-space: pre-wrap; overflow-wrap: anywhere", styles);
        Assert.Contains(".scene-readable-copy p.collapsed", styles);
        Assert.Contains("font-weight: 400; line-height: 1.6", styles);
        Assert.DoesNotContain(".scene-copy-grid p { display: -webkit-box", styles, StringComparison.Ordinal);
    }

    [Fact]
    public void TopbarControls_KeepCompactSingleLineText()
    {
        var styles = ReadRepositoryFile("TOOL-LOCAL", "Web", "src", "styles.css");

        Assert.Contains(".upgrade-button { display: flex; flex: 0 0 auto", styles);
        Assert.Contains("font-size: 12px; font-weight: 650; line-height: 1; white-space: nowrap", styles);
        Assert.Contains(".project-picker { position: relative; display: flex; flex: 0 1 235px", styles);
        Assert.Contains(".project-picker span { flex: 0 0 auto", styles);
        Assert.Contains(".project-picker select { flex: 1 1 auto; min-width: 0; width: 100%", styles);
        Assert.Contains("text-overflow: ellipsis; white-space: nowrap", styles);
    }

    [Fact]
    public void WorkspaceSide_RemainsVisibleWhileMainContentScrolls()
    {
        var styles = ReadRepositoryFile("TOOL-LOCAL", "Web", "src", "styles.css");

        Assert.Contains(".workspace-side { position: sticky; top: 106px", styles);
        Assert.Contains("max-height: calc(100vh - 120px); overflow-y: auto", styles);
        Assert.Contains(".workspace-side { position: static; display: grid", styles);
    }

    [Fact]
    public void VideoGeneration_SendsOnlySelectedSceneIdsAndDoesNotUseHardCodedPrice()
    {
        var app = ReadRepositoryFile("TOOL-LOCAL", "Web", "src", "App.tsx");
        var bridge = ReadRepositoryFile("TOOL-LOCAL", "WebView", "DashboardBridge.cs");
        var generation = ReadRepositoryFile("TOOL-LOCAL", "Generation", "ProjectGenerationService.cs");

        Assert.Contains("postToHost('generation.video', { sceneIds })", app);
        Assert.Contains("rate Active và giữ trong budget tổ chức", app);
        Assert.DoesNotContain("0.084", app, StringComparison.Ordinal);
        Assert.Contains("GenerateVideoWebRequest", bridge);
        Assert.Contains("requestedSceneIds.Contains(x.SceneId)", generation);
        Assert.Contains("Danh sách cảnh chứa cảnh không thuộc kế hoạch hiện hành", generation);
    }

    [Fact]
    public void VideoGeneration_PreflightsMediaToolsAndCanResumeCompletedRequest()
    {
        var app = ReadRepositoryFile("TOOL-LOCAL", "Web", "src", "App.tsx");
        var bridge = ReadRepositoryFile("TOOL-LOCAL", "WebView", "DashboardBridge.cs");
        var form = ReadRepositoryFile("TOOL-LOCAL", "Form1.cs");
        var generation = ReadRepositoryFile("TOOL-LOCAL", "Generation", "ProjectGenerationService.cs");

        var preflightIndex = generation.IndexOf(
            "await mediaToolPreflight.RequireReadyAsync(cancellationToken)",
            StringComparison.Ordinal);
        var resumeIndex = generation.IndexOf(
            "apiClient.GetVideoStatusAsync(existingRequest.ProviderRequestId",
            StringComparison.Ordinal);
        var submitIndex = generation.IndexOf(
            "apiClient.SubmitVideoAsync",
            StringComparison.Ordinal);

        Assert.True(preflightIndex >= 0 && preflightIndex < submitIndex);
        Assert.True(resumeIndex >= 0 && resumeIndex < submitIndex);
        Assert.Contains("existingRequest.Status is not (\"Failed\" or \"Cancelled\" or \"Expired\")", generation);
        Assert.Contains("generation.Status = \"Generated\"", generation);
        Assert.Contains("media.tools.check", bridge);
        Assert.Contains("media.tools.install", app);
        Assert.Contains("media.tools.install.prepare", app);
        Assert.Contains("media.tools.install.available", app);
        Assert.Contains("case \"media.tools.install\"", form);
        Assert.Contains("GetRepairReleaseAsync", form);
        Assert.Contains("Cài bộ xử lý video", app);
        Assert.Contains("MediaToolInstallModal", app);
        Assert.Contains("Tiếp tục tải clip", app);
        Assert.Contains("!mediaTools.ready", app);
    }

    [Fact]
    public void VideoGeneration_ResumeConfirmationDoesNotPresentDownloadAsANewPaidRequest()
    {
        var app = ReadRepositoryFile("TOOL-LOCAL", "Web", "src", "App.tsx");
        var styles = ReadRepositoryFile("TOOL-LOCAL", "Web", "src", "styles.css");

        Assert.Contains("const resumableScenes = selectedScenes.filter(sceneNeedsLocalCompletion);", app);
        Assert.Contains("const newRequestScenes = selectedScenes.filter((scene) => !sceneNeedsLocalCompletion(scene));", app);
        Assert.Contains("estimatedVideoCostPerSecond * newRequestSeconds", app);
        Assert.Contains("XÁC NHẬN TẢI CLIP", app);
        Assert.Contains("XÁC NHẬN TẢI VÀ TẠO CLIP", app);
        Assert.Contains("không gửi yêu cầu tạo video mới và không phát sinh chi phí provider mới", app);
        Assert.Contains("Tải ${selectedDownloadCount} clip đã tạo", app);
        Assert.Contains("confirmation-note-info", styles);
    }

    [Fact]
    public void SceneEditing_CreatesNewPromptVersionAndBlocksEditingAfterProviderSubmission()
    {
        var app = ReadRepositoryFile("TOOL-LOCAL", "Web", "src", "App.tsx");
        var bridge = ReadRepositoryFile("TOOL-LOCAL", "WebView", "DashboardBridge.cs");
        var service = ReadRepositoryFile("TOOL-LOCAL", "Projects", "ProjectService.cs");

        Assert.Contains("postToHost('scene.update', payload)", app);
        Assert.Contains("case \"scene.update\"", bridge);
        Assert.Contains("previousPrompt.Status = \"Superseded\"", service);
        Assert.Contains("Version = previousPrompt.Version + 1", service);
        Assert.Contains("scene.ProviderRequests.Any", service);
        Assert.Contains("Cảnh đã được gửi sang provider video", service);
    }

    [Fact]
    public void SceneEditing_KeepsTheDraftOpenAndShowsTheActualSaveResult()
    {
        var app = ReadRepositoryFile("TOOL-LOCAL", "Web", "src", "App.tsx");

        Assert.Contains("pendingSceneSaveRef", app);
        Assert.Contains("pendingSceneSave.requestId === message.requestId", app);
        Assert.Contains("status: 'failed'", app);
        Assert.Contains("saveState?.status === 'succeeded'", app);
        Assert.Contains("Nội dung bạn vừa sửa vẫn được giữ lại", app);
        Assert.Contains("scene-save-feedback error", app);
        Assert.Contains("Đang lưu cảnh. Vui lòng chờ xác nhận từ desktop.", app);
    }

    [Fact]
    public void Storyboard_DoesNotExposeOrEnforceLegacySpeechWordBudget()
    {
        var app = ReadRepositoryFile("TOOL-LOCAL", "Web", "src", "App.tsx");
        var types = ReadRepositoryFile("TOOL-LOCAL", "Web", "src", "types.ts");
        var dashboard = ReadRepositoryFile("TOOL-LOCAL", "Projects", "ProjectService.cs");
        var generation = ReadRepositoryFile("TOOL-LOCAL", "Generation", "ProjectGenerationService.cs");

        Assert.DoesNotContain("maximumSpokenWords", types);
        Assert.DoesNotContain("scene.maximumSpokenWords", app);
        Assert.Contains("status === 'promptinvalid'", app);
        Assert.DoesNotContain("speechWordBudgetExceeded", app);
        Assert.DoesNotContain("IsSpeechWordBudgetError", dashboard);
        Assert.DoesNotContain("MarkSceneSpeechValidationFailedAsync", generation);
        Assert.DoesNotContain("sceneWithSpeechOverBudget", generation);
        Assert.DoesNotContain("kling_spoken_text_too_long", generation);
        Assert.Contains("const selectable = canQueueScene(scene) &&", app);
        Assert.Contains("x.Status == \"Failed\"", generation);
        Assert.Contains("$\"{keyPrefix}:retry:{failedAttempts}\"", generation);
    }

    [Fact]
    public void VideoOutputCacheRetry_IsPersistedAndShownWithoutCreatingANewTask()
    {
        var app = ReadRepositoryFile("TOOL-LOCAL", "Web", "src", "App.tsx");
        var dashboard = ReadRepositoryFile("TOOL-LOCAL", "Projects", "ProjectService.cs");
        var generation = ReadRepositoryFile("TOOL-LOCAL", "Generation", "ProjectGenerationService.cs");

        Assert.Contains("task.ErrorCode", generation, StringComparison.Ordinal);
        Assert.Contains("task.ErrorMessage", generation, StringComparison.Ordinal);
        Assert.Contains("provider_output_download_failed", dashboard, StringComparison.Ordinal);
        Assert.Contains("Đang lưu clip", app, StringComparison.Ordinal);
        Assert.Contains("Đang kết nối lại", app, StringComparison.Ordinal);
        Assert.Contains("existingRequest.Status is not (\"Failed\" or \"Cancelled\" or \"Expired\")", generation, StringComparison.Ordinal);
    }

    [Fact]
    public void SceneSave_MapsPayloadAndDatabaseFailuresWithoutDiscardingTheDraft()
    {
        var app = ReadRepositoryFile("TOOL-LOCAL", "Web", "src", "App.tsx");
        var bridge = ReadRepositoryFile("TOOL-LOCAL", "WebView", "DashboardBridge.cs");

        Assert.Contains("workflow_data_conflict", bridge);
        Assert.Contains("workflow_save_failed", bridge);
        Assert.Contains("invalid_payload", bridge);
        Assert.Contains("Nội dung bạn vừa nhập vẫn được giữ", bridge);
        Assert.Contains("status: 'failed'", app);
        Assert.Contains("scene-save-feedback error", app);
    }

    [Fact]
    public void CharacterConsistency_RequiresReferenceApprovalBeforeSceneGeneration()
    {
        var app = ReadRepositoryFile("TOOL-LOCAL", "Web", "src", "App.tsx");
        var types = ReadRepositoryFile("TOOL-LOCAL", "Web", "src", "types.ts");
        var bridge = ReadRepositoryFile("TOOL-LOCAL", "WebView", "DashboardBridge.cs");
        var projectService = ReadRepositoryFile("TOOL-LOCAL", "Projects", "ProjectService.cs");
        var generationService = ReadRepositoryFile("TOOL-LOCAL", "Generation", "ProjectGenerationService.cs");

        Assert.Contains("function CharacterSection", app);
        Assert.Contains("character.reference.select", app);
        Assert.Contains("character.approve", app);
        Assert.Contains("scene.characterSetupMessage", app);
        Assert.Contains("characters: CharacterSummary[]", types);
        Assert.Contains("case \"character.reference.select\"", bridge);
        Assert.Contains("reference.ApprovalStatus == \"Approved\"", projectService);
        Assert.Contains("scene.Character is { Status: not \"Approved\" }", generationService);
        Assert.Contains("new VideoReferenceImageInput", generationService);
    }

    [Fact]
    public void GenerationConfirmations_UseThemedAccessibleModalInsteadOfBrowserDialog()
    {
        var app = ReadRepositoryFile("TOOL-LOCAL", "Web", "src", "App.tsx");
        var styles = ReadRepositoryFile("TOOL-LOCAL", "Web", "src", "styles.css");

        Assert.Contains("function ConfirmationModal", app);
        Assert.Contains("role=\"alertdialog\"", app);
        Assert.Contains("aria-modal=\"true\"", app);
        Assert.Contains("event.key === 'Escape'", app);
        Assert.Contains("Sinh lại content có nhân vật?", app);
        Assert.DoesNotContain("window.confirm", app, StringComparison.Ordinal);
        Assert.Contains(".confirmation-overlay { position: fixed; z-index: 1100", styles);
        Assert.Contains("backdrop-filter: blur(6px)", styles);
    }

    [Fact]
    public void ProviderUnavailableError_UsesCustomerFriendlyMaintenancePopup()
    {
        var app = ReadRepositoryFile("TOOL-LOCAL", "Web", "src", "App.tsx");
        var styles = ReadRepositoryFile("TOOL-LOCAL", "Web", "src", "styles.css");

        Assert.Contains("message.error?.code === 'provider_temporarily_unavailable'", app);
        Assert.Contains("function ServiceErrorModal", app);
        Assert.Contains("Máy chủ đang bảo trì", app);
        Assert.Contains("Đã hiểu", app);
        Assert.Contains("role=\"alertdialog\"", app);
        Assert.Contains(".service-error-card::before", styles);
        Assert.DoesNotContain("Account balance not enough", app, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SceneAssetPicker_UsesSingleBackgroundAndOneClickSceneConfirmation()
    {
        var app = ReadRepositoryFile("TOOL-LOCAL", "Web", "src", "App.tsx");
        var bridge = ReadRepositoryFile("TOOL-LOCAL", "WebView", "DashboardBridge.cs");

        Assert.Contains("type={assetType === 'Background' ? 'radio' : 'checkbox'}", app);
        Assert.Contains("Cảnh có tài sản phải chọn đúng một bối cảnh.", app);
        Assert.Contains("Xác nhận tài sản cảnh", app);
        Assert.Contains("Áp dụng lựa chọn", app);
        Assert.Contains("Phần bắt buộc:", app);
        Assert.DoesNotContain("Prompt Kling hiện tại", app, StringComparison.Ordinal);
        Assert.Contains("Duyệt & khóa", app);
        Assert.Contains("project-assets.approve-ai", app);
        Assert.Contains("scene.assets.confirm", app);
        Assert.Contains("case \"project-assets.approve-ai\"", bridge);
        Assert.Contains("case \"scene.assets.confirm\"", bridge);
    }

    private static string ReadRepositoryFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(relativeParts).ToArray());
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate).Replace("\r\n", "\n", StringComparison.Ordinal);
            }
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Cannot locate repository file: {Path.Combine(relativeParts)}");
    }
}
