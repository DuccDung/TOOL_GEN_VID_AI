using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using TOOL_LOCAL.Authentication;
using TOOL_LOCAL.Configuration;
using TOOL_LOCAL.Data;
using TOOL_LOCAL.Vietsub.Jobs;
using TOOL_LOCAL.Vietsub.Storage;
using TOOL_LOCAL.WebView;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Generation;
using TOOL_SERVER.Infrastructure;
using TOOL_SHARED.Contracts.Common;

namespace TOOL_TESTS.Vietsub;

public sealed class VietsubLocalOnlyTests
{
    [Theory]
    [InlineData("project.create")]
    [InlineData("project.select")]
    [InlineData("short-video.generate")]
    [InlineData("generation.content")]
    [InlineData("generation.content.repair")]
    [InlineData("generation.video")]
    [InlineData("character.reference.generate")]
    [InlineData("voice-catalog.preview")]
    [InlineData("scene.speech.verify")]
    [InlineData("render.final")]
    [InlineData("desktop.settings.update")]
    public async Task DesktopBridge_RejectsVideoCommandsBeforeCallingAnyService(string command)
    {
        var messages = new List<string>();
        using var bridge = new DashboardBridge(null!, null!, null!, null!, null!, null!, null!, null!, true,
            messages.Add, () => { }, localOnly: true);
        await bridge.HandleAsync(JsonSerializer.Serialize(new { type = command, requestId = "blocked", payload = new { } }));
        using var result = JsonDocument.Parse(Assert.Single(messages));
        Assert.Equal("blocked", result.RootElement.GetProperty("requestId").GetString());
        Assert.Equal(ApplicationFeaturePolicy.LocalOnlyErrorCode, result.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("/api/generation/content", "POST", true)]
    [InlineData("/api/generation/voices/preview", "POST", true)]
    [InlineData("/api/projects/fixture/assets", "PUT", true)]
    [InlineData("/api/projects/fixture/scenes/fixture/first-frames", "POST", true)]
    [InlineData("/api/vietsub/projects/fixture/cloud-translation/jobs", "POST", true)]
    [InlineData("/api/vietsub/projects/fixture/cloud-translation/jobs/fixture/retry", "POST", true)]
    [InlineData("/api/vietsub/projects/fixture/cloud-translation/jobs/fixture/resume", "POST", true)]
    [InlineData("/api/vietsub/projects/fixture/cloud-translation/jobs/fixture/cancel", "POST", false)]
    [InlineData("/api/vietsub/projects/fixture/cloud-translation/jobs/fixture/ack", "POST", false)]
    [InlineData("/api/organizations", "GET", false)]
    [InlineData("/api/vietsub/projects", "POST", false)]
    [InlineData("/api/license/heartbeat", "POST", false)]
    [InlineData("/api/generation/videos/fixture", "GET", false)]
    public async Task ServerFilter_BlocksSubmissionsAndPreservesLocalAuthorizationAndReconciliation(string path, string method, bool blocked)
    {
        var http = new DefaultHttpContext(); http.Request.Path = path; http.Request.Method = method;
        var action = new ActionContext(http, new RouteData(), new ActionDescriptor());
        var filters = new List<IFilterMetadata>();
        var context = new ResourceExecutingContext(action, filters, new List<IValueProviderFactory>());
        var invoked = false;
        await new LocalOnlyFeatureFilter(Options.Create(new ApplicationFeaturePolicy())).OnResourceExecutionAsync(context, () =>
        {
            invoked = true;
            return Task.FromResult(new ResourceExecutedContext(action, filters));
        });
        Assert.Equal(!blocked, invoked);
        if (blocked)
        {
            var result = Assert.IsType<ObjectResult>(context.Result);
            Assert.Equal(403, result.StatusCode);
            Assert.Equal(ApplicationFeaturePolicy.LocalOnlyErrorCode, Assert.IsType<ApiErrorResponse>(result.Value).Code);
        }
    }

    [Theory]
    [InlineData("/api/generation/content", "POST")]
    [InlineData("/api/projects/fixture/assets", "GET")]
    [InlineData("/api/vietsub/projects/fixture/cloud-translation/jobs", "POST")]
    public async Task DesktopGateway_BlocksVideoAndCloudBeforeNetwork(string path, string method)
    {
        var transport = new CountingHandler();
        using var client = new HttpClient(new LocalOnlyGatewayHandler(new()) { InnerHandler = transport });
        var error = await Assert.ThrowsAsync<AccountClientException>(() => client.SendAsync(new(new(method), "https://fixture.test" + path)));
        Assert.Equal(ApplicationFeaturePolicy.LocalOnlyErrorCode, error.Code);
        Assert.Equal(0, transport.Calls);
    }

    [Theory]
    [InlineData("POST", "/v1/responses", true)]
    [InlineData("POST", "/v1/audio/speech", true)]
    [InlineData("POST", "/v1/videos/text2video", true)]
    [InlineData("GET", "/v1/videos/existing", false)]
    [InlineData("POST", "/requests/existing/cancel", false)]
    public async Task ProviderTransport_BlocksNewInferenceButPreservesExistingRequests(string method, string path, bool blocked)
    {
        var transport = new CountingHandler();
        using var client = new HttpClient(new LocalOnlyProviderHandler(Options.Create(new ApplicationFeaturePolicy())) { InnerHandler = transport });
        var request = new HttpRequestMessage(new(method), "https://fixture.test" + path);
        if (blocked)
            Assert.Equal(ApplicationFeaturePolicy.LocalOnlyErrorCode,
                (await Assert.ThrowsAsync<AccountApiException>(() => client.SendAsync(request))).Code);
        else
            using (await client.SendAsync(request)) { }
        Assert.Equal(blocked ? 0 : 1, transport.Calls);
    }

    [Fact]
    public async Task Resolver_RejectsNewRuntimeBeforeCredentialOrDatabaseAccess()
    {
        var resolver = new ProviderRuntimeResolver(null!, null!, null!, Options.Create(new ApplicationFeaturePolicy()));
        var error = await Assert.ThrowsAsync<AccountApiException>(() => resolver.ResolveAsync(Guid.NewGuid(), "openai", "Text", null, default));
        Assert.Equal(ApplicationFeaturePolicy.LocalOnlyErrorCode, error.Code);
    }

    [Fact]
    public async Task JobManager_BlocksCloudRecoveryWithoutChangingCheckpointAndStillAcceptsLocalJobs()
    {
        var root = Path.Combine(Path.GetTempPath(), "vs-local-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new VietsubAppPaths(root);
            var store = new VietsubJobStore(paths, new VietsubSubtitleStore(paths));
            var project = Guid.NewGuid();
            var oldJob = await store.CreateAsync(project, VietsubJobTypes.TranslateCloud, ["CLOUD_TRANSLATE"]);
            await store.TransitionAsync(project, oldJob.Id, VietsubJobStatus.Running, "STARTED");
            await store.TransitionAsync(project, oldJob.Id, VietsubJobStatus.Interrupted, "INTERRUPTED");
            await using var manager = new VietsubJobManager(store, new VietsubJobExecutorRegistry(), localOnly: true);
            var error = await Assert.ThrowsAsync<VietsubJobException>(() => manager.ResumeAsync(project, oldJob.Id));
            Assert.Equal(ApplicationFeaturePolicy.LocalOnlyErrorCode, error.Code);
            Assert.Equal(VietsubJobStatus.Interrupted, (await store.GetAsync(project, oldJob.Id))!.Status);
            await manager.CancelAsync(project, oldJob.Id);
            await Assert.ThrowsAsync<VietsubJobException>(() => manager.EnqueueAsync(project, VietsubJobTypes.TranslateCloud, ["CLOUD_TRANSLATE"]));
            var local = await manager.EnqueueAsync(project, VietsubJobTypes.TranslateLocal, ["TRANSLATE"], startImmediately: false);
            Assert.Equal("TRANSLATE_LOCAL", local.Type);
            await manager.CancelAsync(project, local.Id);
            var voice = await manager.EnqueueAsync(project, VietsubJobTypes.SynthesizeVoiceLocal, ["VOICE"], startImmediately: false);
            Assert.Equal(VietsubJobTypes.SynthesizeVoiceLocal, voice.Type);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void LocalOnlyOptions_AllowMissingWorkflowSqlAndDatabaseFactoryRejectsAccidentalAccess()
    {
        var root = Path.Combine(Path.GetTempPath(), "vs-options-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "appsettings.json"), """
                { "Server": { "BaseUrl": "https://fixture.test" }, "Storage": { "WorkspaceRoot": "workspace" } }
                """);
            Assert.True(DesktopOptions.Load(root).Application.VietsubLocalOnly);
            Assert.Throws<AccountClientException>(() => new VideoFactoryDbContextFactory("", disabled: true).CreateDbContext());
            File.WriteAllText(Path.Combine(root, "appsettings.user.json"), """{ "Application": { "VietsubLocalOnly": false } }""");
            Assert.Throws<InvalidOperationException>(() => DesktopOptions.Load(root));
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Calls++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)); }
    }
}
