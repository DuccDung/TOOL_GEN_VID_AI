using TOOL_LOCAL.Authentication;
using TOOL_LOCAL.Configuration;
using TOOL_LOCAL.Data;
using TOOL_LOCAL.Projects;
using TOOL_LOCAL.Storage;
using TOOL_LOCAL.Updates;
using TOOL_LOCAL.Generation;
using TOOL_LOCAL.Media;
using TOOL_LOCAL.Providers;
using TOOL_LOCAL.Vietsub.Storage;
using TOOL_LOCAL.Vietsub.Api;
using TOOL_LOCAL.Vietsub.Media;
using TOOL_LOCAL.Vietsub.Subtitles;
using TOOL_LOCAL.Vietsub.Jobs;
using TOOL_LOCAL.Vietsub.Ocr;
using TOOL_LOCAL.Vietsub.Translation;
using TOOL_LOCAL.Vietsub.Voice;
using TOOL_LOCAL.Payments;
using TOOL_LOCAL.TikTok;

namespace TOOL_LOCAL;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (TOOL_LOCAL.LocalVoice.LocalVoiceMaintenance.IsMaintenanceCommand(args))
        {
            Environment.ExitCode = TOOL_LOCAL.LocalVoice.LocalVoiceMaintenance.RunAsync(args[0]).GetAwaiter().GetResult();
            return;
        }
        ApplicationConfiguration.Initialize();

        try
        {
            var options = DesktopOptions.Load();
            LegacyProviderCredentialCleaner.Remove();
            using var httpClient = new HttpClient
            {
                BaseAddress = new Uri(options.Server.BaseUrl),
                Timeout = TimeSpan.FromSeconds(30)
            };
            var apiClient = new AccountApiClient(httpClient);
            using var updateHttpClient = new HttpClient
            {
                BaseAddress = new Uri(options.Server.BaseUrl),
                Timeout = TimeSpan.FromMinutes(30)
            };
            using var generationHttpClient = new HttpClient(new GatewayReadRetryHandler { InnerHandler = new HttpClientHandler() })
            {
                BaseAddress = new Uri(options.Server.BaseUrl),
                Timeout = TimeSpan.FromMinutes(30)
            };
            using var tiktokGatewayHttpClient = new HttpClient(new HttpClientHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false
            })
            {
                BaseAddress = new Uri(options.Server.BaseUrl),
                Timeout = TimeSpan.FromSeconds(30)
            };
            using var tiktokUploadHttpClient = new HttpClient(new HttpClientHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false,
                UseProxy = false
            })
            {
                Timeout = TimeSpan.FromMinutes(30)
            };
            using var sessionManager = new AccountSessionManager(
                apiClient,
                new DpapiTokenStore(),
                new DeviceIdentityService());

            while (true)
            {
                using (var loginForm = new LoginForm(sessionManager))
                {
                    if (loginForm.ShowDialog() != DialogResult.OK || sessionManager.Current is null)
                    {
                        return;
                    }
                }

                var licenseApiClient = new LicenseApiClient(httpClient, sessionManager);
                var licenseManager = new LicenseSessionManager(licenseApiClient);
                var licensePaymentClient = new LicensePaymentApiClient(httpClient, sessionManager);
                try
                {
                    licenseManager.InitializeAsync().GetAwaiter().GetResult();
                }
                catch (AccountClientException exception)
                {
                    licenseManager.DisposeAsync().AsTask().GetAwaiter().GetResult();
                    if (!sessionManager.IsAuthenticated)
                    {
                        MessageBox.Show(
                            AccountSessionManager.SessionExpiredMessage,
                            "Phiên đăng nhập đã hết hạn",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                        continue;
                    }

                    MessageBox.Show(
                        exception.Message,
                        "License chưa sẵn sàng",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                var dbContextFactory = new VideoFactoryDbContextFactory(options.Database.ConnectionString);
                var workspaceService = new ProjectWorkspaceService(options.Storage.WorkspaceRoot);
                var mediaProcessRunner = new ExternalProcessRunner();
                var mediaToolPaths = new MediaToolPathResolver(options.MediaTools).Resolve();
                var mediaToolPreflight = new MediaToolPreflightService(
                    mediaToolPaths,
                    mediaProcessRunner,
                    TimeProvider.System);
                var mediaProbe = new FfprobeService(mediaToolPaths.FfprobePath, mediaProcessRunner);
                var audioQualityValidator = new AudioQualityValidator(
                    mediaToolPaths.FfmpegPath,
                    mediaProcessRunner,
                    mediaProbe);
                var sceneAudioMixer = new SceneAudioMixer(
                    mediaToolPaths.FfmpegPath,
                    mediaProcessRunner,
                    mediaProbe,
                    audioQualityValidator,
                    new SceneAudioMixerOptions
                    {
                        MaximumTempoAdjustmentRatio = options.SpeechSynchronization.MaximumTempoAdjustmentRatio,
                        MinimumVoiceDurationRatio = options.SpeechSynchronization.MinimumVoiceDurationRatio,
                        TargetLoudnessLufs = options.SpeechSynchronization.TargetLoudnessLufs,
                        TargetSpeechLeadInMs = options.SpeechSynchronization.TargetSpeechLeadInMs,
                        SpeechBoundaryPaddingMs = options.SpeechSynchronization.SpeechBoundaryPaddingMs
                    });
                var speechAudioExtractor = new SpeechAudioExtractor(
                    mediaToolPaths.FfmpegPath,
                    mediaProcessRunner,
                    mediaProbe);
                var sceneVideoTrimmer = new SceneVideoTrimmer(
                    mediaToolPaths.FfmpegPath,
                    mediaProcessRunner);
                var finalMediaRenderer = new FfmpegRenderService(
                    mediaToolPaths.FfmpegPath,
                    mediaProcessRunner);
                var finalOutputInspector = new FinalOutputInspector(
                    mediaProbe,
                    audioQualityValidator);
                var generationClient = new ServerGenerationClient(
                    generationHttpClient, sessionManager, licenseManager);
                var shortVideoOutfit = new ShortVideoWorkflowService(dbContextFactory, workspaceService, generationClient, options.Features.ShortVideoCharacterOutfitEnabled);
                var projectService = new ProjectService(dbContextFactory, workspaceService, options.Features.SpeechSynchronizationEnabled, shortVideoOutfit);
                using var localVoiceService = new TOOL_LOCAL.LocalVoice.LocalVoiceService(
                    dbContextFactory, new TOOL_LOCAL.LocalVoice.LocalVoiceStore(workspaceService),
                    new TOOL_LOCAL.LocalVoice.LocalVoiceRuntime(workspaceService.WorkspaceRoot, options.Features.VeoLocalVoiceConsistencyEnabled,
                        componentRootOverride: options.LocalVoice.ComponentRoot, temporaryRootOverride: options.LocalVoice.TemporaryRoot),
                    new TOOL_LOCAL.LocalVoice.LocalVoiceMedia(mediaToolPaths.FfmpegPath, mediaProcessRunner, mediaProbe, audioQualityValidator),
                    generationClient);
                var projectRenderService = new ProjectRenderService(
                    dbContextFactory,
                    workspaceService,
                    mediaToolPreflight,
                    finalMediaRenderer,
                    finalOutputInspector,
                    options.Features.SpeechSynchronizationEnabled,
                    options.SpeechSynchronization.TargetLoudnessLufs,
                    localVoiceService,
                    shortVideoOutfit);
                var generationService = new ProjectGenerationService(
                    dbContextFactory,
                    workspaceService,
                    generationClient,
                    mediaProbe,
                    mediaToolPreflight,
                    audioQualityValidator,
                    sceneAudioMixer,
                    sceneVideoTrimmer,
                    speechAudioExtractor,
                    shortVideoOutfit);
                var tiktokGatewayClient = new TikTokGatewayClient(
                    tiktokGatewayHttpClient,
                    sessionManager,
                    licenseManager);
                var tiktokOAuthCoordinator = new TikTokOAuthCoordinator(tiktokGatewayClient);
                var tiktokMediaService = new TikTokMediaService(mediaProbe, mediaToolPreflight);
                var tiktokPreviewService = new TikTokMediaPreviewService(tiktokMediaService);
                var tiktokUploadService = new TikTokUploadService(tiktokUploadHttpClient);
                var updateApiClient = new DesktopUpdateApiClient(updateHttpClient, sessionManager, options.Update);
                var packageUpdateService = new DesktopPackageUpdateService(updateHttpClient);
                VietsubProjectStore? vietsubProjectStore = null;
                IVietsubProjectRegistryClient? vietsubProjectRegistryClient = null;
                VietsubMediaImportService? vietsubMediaImportService = null;
                VietsubTimelineThumbnailService? vietsubThumbnailService = null;
                VietsubTimelineWaveformService? vietsubWaveformService = null;
                VietsubSubtitleService? vietsubSubtitleService = null;
                VietsubJobManager? vietsubJobManager = null;
                VietsubOcrService? vietsubOcrService = null;
                VietsubTranslationService? vietsubTranslationService = null;
                VietsubCloudTranslationService? vietsubCloudTranslationService = null;
                QwenGgufVietsubTranslationProvider? vietsubTranslationProvider = null;
                VietsubVoiceService? vietsubVoiceService = null;
                VietsubVideoExportService? vietsubVideoExportService = null;
                VietsubVoiceComponentStore? vietsubVoiceComponents = null;
                if (options.Features.VietsubEnabled)
                {
                    var vietsubPaths = new VietsubAppPaths(options.Storage.WorkspaceRoot);
                    var vietsubSubtitleStore = new VietsubSubtitleStore(vietsubPaths);
                    vietsubProjectStore = new VietsubProjectStore(vietsubPaths, vietsubSubtitleStore);
                    vietsubSubtitleService = new VietsubSubtitleService(vietsubPaths, vietsubSubtitleStore);
                    vietsubProjectRegistryClient = new VietsubProjectRegistryClient(
                        generationHttpClient,
                        sessionManager,
                        licenseManager);
                    vietsubMediaImportService = new VietsubMediaImportService(
                        vietsubPaths,
                        mediaToolPreflight,
                        mediaProbe);
                    var vietsubJobStore = new VietsubJobStore(vietsubPaths, vietsubSubtitleStore);
                    IVietsubOcrRecognizer ocrRecognizer = options.Features.VietsubOcrEnabled
                        ? new PaddleVietsubOcrRecognizer()
                        : new UnavailableVietsubOcrRecognizer(
                            "OCR_FEATURE_DISABLED",
                            "OCR local đang bị khóa bởi feature flag cho tới khi runtime và package gate được duyệt.");
                    var ocrFrameReader = new VietsubFfmpegFrameReader(
                        mediaToolPaths.FfmpegPath,
                        mediaToolPreflight);
                    var ocrExecutor = new VietsubOcrJobExecutor(
                        vietsubProjectStore,
                        vietsubMediaImportService,
                        ocrFrameReader,
                        ocrRecognizer,
                        vietsubSubtitleStore,
                        vietsubJobStore,
                        vietsubPaths);
                    var translationStore = new VietsubTranslationStore(
                        vietsubPaths,
                        vietsubSubtitleStore);
                    if (options.Features.VietsubLocalTranslationEnabled)
                    {
                        vietsubTranslationProvider = new QwenGgufVietsubTranslationProvider(
                            new VietsubTranslationComponentStore(
                                VietsubTranslationApprovedComponents.Qwen3_4B_Q4Km));
                    }
                    var translationProviderRegistry = new VietsubTranslationProviderRegistry(
                        vietsubTranslationProvider is null ? [] : [vietsubTranslationProvider],
                        featureEnabled: options.Features.VietsubLocalTranslationEnabled);
                    var translationExecutor = new VietsubTranslationJobExecutor(
                        vietsubProjectStore,
                        vietsubSubtitleStore,
                        translationStore,
                        translationProviderRegistry,
                        vietsubJobStore,
                        vietsubPaths);
                    var voiceStore = new VietsubVoiceStore(vietsubPaths, vietsubSubtitleStore);
                    var voicePlaybackRegistry = new VietsubVoicePlaybackRegistry(voiceStore.IsTrackRevisionCurrent);
                    vietsubVoiceComponents = new VietsubVoiceComponentStore(
                        vietsubPaths,
                        options.Features.VietsubLocalVoiceEnabled);
                    var voiceSynthesizer = new VietsubPiperVoiceSynthesizer(vietsubVoiceComponents);
                    var voiceTimelineRenderer = new VietsubVoiceTimelineRenderer(
                        vietsubPaths,
                        mediaToolPreflight,
                        mediaToolPaths.FfmpegPath,
                        mediaProcessRunner);
                    var voiceExecutor = new VietsubVoiceJobExecutor(
                        vietsubProjectStore,
                        vietsubSubtitleStore,
                        voiceStore,
                        voiceSynthesizer,
                        voiceTimelineRenderer,
                        vietsubJobStore,
                        vietsubPaths);
                    var localJobAuthorizer = new VietsubLocalJobAuthorizer(
                        new DesktopVietsubLocalAccessContext(
                            sessionManager,
                             licenseManager,
                             generationClient));
                    var cloudClient = new VietsubCloudTranslationClient(generationHttpClient, sessionManager, licenseManager);
                    var cloudExecutor = new VietsubCloudTranslationJobExecutor(vietsubProjectStore, vietsubSubtitleStore,
                        translationStore, vietsubPaths, localJobAuthorizer, cloudClient, vietsubJobStore);
                    vietsubJobManager = new VietsubJobManager(vietsubJobStore,
                        new VietsubJobExecutorRegistry([ocrExecutor, translationExecutor, cloudExecutor, voiceExecutor]));
                    vietsubCloudTranslationService = new VietsubCloudTranslationService(localJobAuthorizer, cloudClient,
                        vietsubSubtitleStore, vietsubPaths, vietsubJobStore, vietsubJobManager);
                    vietsubVideoExportService = new VietsubVideoExportService(
                        localJobAuthorizer,
                        vietsubProjectStore,
                        vietsubMediaImportService,
                        vietsubSubtitleStore,
                        voiceStore,
                        vietsubPaths,
                        mediaToolPreflight,
                        mediaProbe,
                        mediaToolPaths.FfmpegPath,
                        mediaProcessRunner);
                    vietsubOcrService = new VietsubOcrService(
                        localJobAuthorizer,
                        vietsubMediaImportService,
                        ocrFrameReader,
                        ocrRecognizer,
                        vietsubJobManager);
                    vietsubTranslationService = new VietsubTranslationService(
                        localJobAuthorizer,
                        vietsubSubtitleStore,
                        translationProviderRegistry,
                        vietsubJobManager);
                    vietsubVoiceService = new VietsubVoiceService(
                        localJobAuthorizer,
                        vietsubSubtitleStore,
                        voiceStore,
                        vietsubPaths,
                        vietsubVoiceComponents,
                        voicePlaybackRegistry,
                        voiceTimelineRenderer,
                        vietsubJobManager);
                    vietsubThumbnailService = new VietsubTimelineThumbnailService(
                        vietsubPaths,
                        vietsubMediaImportService,
                        mediaToolPreflight,
                        mediaToolPaths.FfmpegPath,
                        mediaProcessRunner);
                    vietsubWaveformService = new VietsubTimelineWaveformService(
                        vietsubPaths,
                        vietsubMediaImportService,
                        mediaToolPreflight,
                        mediaToolPaths.FfmpegPath,
                        mediaProcessRunner);
                }
                var bilibiliRuntime = new Bilibili.BilibiliRuntime();
                var bilibiliDownloader = new Bilibili.BilibiliDownloader(bilibiliRuntime,
                    new Bilibili.BilibiliProcessRunner(), new Bilibili.BilibiliMediaVerifier(mediaToolPreflight, mediaProbe), mediaToolPaths.FfmpegPath);
                using var bilibiliService = new Bilibili.BilibiliService(bilibiliRuntime, bilibiliDownloader,
                    async token => { await licenseManager.EnsureAccessAsync(token); });
                using var mainForm = new Form1(
                    sessionManager,
                    licenseManager,
                    projectService,
                    projectRenderService,
                    generationService,
                    generationClient,
                    workspaceService,
                    updateApiClient,
                    packageUpdateService,
                    options.Update,
                    mediaToolPreflight,
                    options.Features,
                    vietsubProjectStore,
                    vietsubProjectRegistryClient,
                    vietsubMediaImportService,
                    vietsubThumbnailService,
                    vietsubWaveformService,
                    vietsubSubtitleService,
                    vietsubJobManager,
                    vietsubOcrService,
                    vietsubTranslationService,
                    vietsubVoiceService,
                    vietsubVideoExportService,
                    tiktokGatewayClient,
                    tiktokOAuthCoordinator,
                    tiktokMediaService,
                    tiktokPreviewService,
                    tiktokUploadService,
                    licensePaymentClient,
                    vietsubCloudTranslationService: vietsubCloudTranslationService,
                    localVoiceService: localVoiceService,
                    shortVideoOutfit: shortVideoOutfit,
                    bilibiliService: bilibiliService);
                try
                {
                    Application.Run(mainForm);
                }
                finally
                {
                    vietsubThumbnailService?.DisposeAsync().AsTask().GetAwaiter().GetResult();
                    vietsubJobManager?.DisposeAsync().AsTask().GetAwaiter().GetResult();
                    vietsubTranslationProvider?.DisposeAsync().AsTask().GetAwaiter().GetResult();
                    vietsubVoiceComponents?.Dispose();
                    licenseManager.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }

                if (!mainForm.ReturnToLoginRequested)
                {
                    return;
                }
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                "Không thể khởi động ứng dụng",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
