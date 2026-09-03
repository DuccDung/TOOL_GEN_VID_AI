using TOOL_LOCAL.Authentication;
using TOOL_LOCAL.Configuration;
using TOOL_LOCAL.Data;
using TOOL_LOCAL.Projects;
using TOOL_LOCAL.Storage;
using TOOL_LOCAL.Updates;
using TOOL_LOCAL.Generation;
using TOOL_LOCAL.Media;
using TOOL_LOCAL.Providers;
using TOOL_LOCAL.Payments;

namespace TOOL_LOCAL;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
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
            using var generationHttpClient = new HttpClient
            {
                BaseAddress = new Uri(options.Server.BaseUrl),
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
                var projectService = new ProjectService(dbContextFactory, workspaceService);
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
                    audioQualityValidator);
                var sceneVideoTrimmer = new SceneVideoTrimmer(
                    mediaToolPaths.FfmpegPath,
                    mediaProcessRunner);
                var finalMediaRenderer = new FfmpegRenderService(
                    mediaToolPaths.FfmpegPath,
                    mediaProcessRunner);
                var finalOutputInspector = new FinalOutputInspector(
                    mediaProbe,
                    audioQualityValidator);
                var projectRenderService = new ProjectRenderService(
                    dbContextFactory,
                    workspaceService,
                    mediaToolPreflight,
                    finalMediaRenderer,
                    finalOutputInspector);
                var generationClient = new ServerGenerationClient(
                    generationHttpClient,
                    sessionManager,
                    licenseManager);
                var generationService = new ProjectGenerationService(
                    dbContextFactory,
                    workspaceService,
                    generationClient,
                    mediaProbe,
                    mediaToolPreflight,
                    audioQualityValidator,
                    sceneAudioMixer,
                    sceneVideoTrimmer);
                var updateApiClient = new DesktopUpdateApiClient(updateHttpClient, sessionManager, options.Update);
                var packageUpdateService = new DesktopPackageUpdateService(updateHttpClient);
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
                    licensePaymentClient);
                try
                {
                    Application.Run(mainForm);
                }
                finally
                {
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
