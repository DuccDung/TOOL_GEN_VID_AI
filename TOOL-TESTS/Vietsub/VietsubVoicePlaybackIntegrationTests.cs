using System.Security.Cryptography;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using TOOL_LOCAL.Vietsub.Domain;
using TOOL_LOCAL.Vietsub.Playback;
using TOOL_LOCAL.Vietsub.Voice;

namespace TOOL_TESTS.Vietsub;

[Collection(NativeWindowsCollection.Name)]
public sealed class VietsubVoicePlaybackIntegrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WebView2_voice_reaches_audio_mixer_and_supports_seeking(bool productionHook)
    {
        var root = Path.Combine(Path.GetTempPath(), "VietsubVoicePlaybackTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            var wavPath = Path.Combine(root, "voice.wav");
            WriteTone(wavPath);
            var project = new VietsubProjectManifest { ProjectId = Guid.NewGuid(), ActiveSubtitleTrackId = Guid.NewGuid() };
            var artifactId = Guid.NewGuid();
            var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(wavPath))).ToLowerInvariant();
            var registry = new VietsubVoicePlaybackRegistry();
            registry.RegisterCurrent(new(project.ProjectId, artifactId, project.ActiveSubtitleTrackId.Value,
                1, wavPath, new FileInfo(wavPath).Length, hash));
            var service = new VietsubMediaPlaybackService(null!, voiceRegistry: registry);
            var url = VietsubVoicePlaybackRegistry.CreateUrl(project.ProjectId, artifactId, hash);
            var webRoot = productionHook ? await BuildProductionHookProbeAsync(root, timeout.Token) : root;
            await File.WriteAllTextAsync(Path.Combine(root, "index.html"), $$"""
                <!doctype html><meta charset="utf-8">
                <audio id="voice" crossorigin="anonymous" preload="auto" src="{{url}}"></audio>
                <script>
                window.run = async () => {
                  try {
                    const audio = document.getElementById('voice');
                    const context = new AudioContext();
                    const source = context.createMediaElementSource(audio);
                    const gain = context.createGain();
                    const analyser = context.createAnalyser();
                    source.connect(gain); gain.connect(analyser); analyser.connect(context.destination);
                    gain.gain.value = 1.49;
                    await context.resume();
                    await audio.play();
                    const samples = new Float32Array(analyser.fftSize);
                    const measure = async () => {
                      let peak = 0;
                      for (let i = 0; i < 15; i++) {
                        await new Promise(resolve => setTimeout(resolve, 50));
                        analyser.getFloatTimeDomainData(samples);
                        peak = Math.max(peak, ...samples.map(Math.abs));
                      }
                      return peak;
                    };
                    const firstPeak = await measure();
                    audio.currentTime = 2;
                    const seekPeak = await measure();
                    audio.pause();
                    await context.close();
                    chrome.webview.postMessage({ firstPeak, seekPeak, time: audio.currentTime });
                  } catch (error) {
                    chrome.webview.postMessage({ error: String(error) });
                  }
                };
                </script>
                """, timeout.Token);

            var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var rangeResponses = 0;
            var thread = new Thread(() =>
            {
                using var form = new Form
                {
                    ShowInTaskbar = false, StartPosition = FormStartPosition.Manual,
                    Location = new Point(-10_000, -10_000), Width = 320, Height = 200
                };
                using var webView = new WebView2 { Dock = DockStyle.Fill };
                form.Controls.Add(webView);
                using var registration = timeout.Token.Register(() =>
                {
                    completion.TrySetException(new TimeoutException("Voice playback did not complete."));
                    if (form.IsHandleCreated) form.BeginInvoke(form.Close);
                });
                form.Shown += async (_, _) =>
                {
                    try
                    {
                        var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: Path.Combine(root, "profile"));
                        await webView.EnsureCoreWebView2Async(environment);
                        var core = webView.CoreWebView2;
                        core.SetVirtualHostNameToFolderMapping("app.local", webRoot, CoreWebView2HostResourceAccessKind.DenyCors);
                        core.AddWebResourceRequestedFilter($"https://{VietsubMediaPlaybackService.HostName}/*",
                            CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);
                        core.WebResourceRequested += (_, args) =>
                        {
                            var uri = new Uri(args.Request.Uri);
                            var range = TOOL_LOCAL.Form1.ReadVietsubRangeHeader(args.Request.Headers,
                                VietsubMediaPlaybackService.ClassifyResource(uri), out var headerError);
                            var response = service.Open(uri, args.Request.Method, range, project);
                            if (response.StatusCode == 206) Interlocked.Increment(ref rangeResponses);
                            args.Response = TOOL_LOCAL.Form1.CreateVietsubWebResourceResponse(environment, response);
                        };
                        core.WebMessageReceived += (_, args) =>
                        {
                            completion.TrySetResult(args.WebMessageAsJson);
                            form.BeginInvoke(form.Close);
                        };
                        core.NavigationCompleted += async (_, _) =>
                        {
                            try
                            {
                                await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate",
                                    JsonSerializer.Serialize(new { expression = $"window.voiceUrl = {JsonSerializer.Serialize(url)}; window.run()", userGesture = true }));
                            }
                            catch (Exception exception) { completion.TrySetException(exception); form.Close(); }
                        };
                        core.Navigate("https://app.local/index.html");
                    }
                    catch (Exception exception) { completion.TrySetException(exception); form.Close(); }
                };
                Application.Run(form);
            }) { IsBackground = true };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            var json = await completion.Task.WaitAsync(timeout.Token);
            Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
            using var result = JsonDocument.Parse(json);
            Assert.False(result.RootElement.TryGetProperty("error", out _), json);
            if (productionHook) Assert.True(result.RootElement.GetProperty("productionHook").GetBoolean(), json);
            else
            {
                Assert.True(result.RootElement.GetProperty("firstPeak").GetDouble() > 0.1, json);
                Assert.True(result.RootElement.GetProperty("seekPeak").GetDouble() > 0.1, json);
                Assert.True(result.RootElement.GetProperty("time").GetDouble() > 2, json);
            }
            Assert.True(rangeResponses > 0, "Voice Range requests must receive partial responses through the native host.");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static async Task<string> BuildProductionHookProbeAsync(string root, CancellationToken token)
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "TOOL_GEN_POST_VIDEO.slnx")))
            repository = repository.Parent;
        Assert.NotNull(repository);
        var web = Path.Combine(repository.FullName, "TOOL-LOCAL", "Web");
        var output = Path.Combine(root, "web");
        var start = new ProcessStartInfo("node")
        {
            WorkingDirectory = web, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in new[] { "node_modules/vite/bin/vite.js", "build", "--config",
                     "tests/voice-playback-browser/vite.config.mjs", "--outDir", output }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(token);
        var stderr = process.StandardError.ReadToEndAsync(token);
        try { await process.WaitForExitAsync(token); }
        catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
        Assert.True(process.ExitCode == 0, (await stdout) + (await stderr));
        return output;
    }

    private static void WriteTone(string path)
    {
        const int sampleRate = 48_000;
        const int samples = sampleRate * 5;
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write("RIFF"u8); writer.Write(36 + samples * 2); writer.Write("WAVEfmt "u8);
        writer.Write(16); writer.Write((short)1); writer.Write((short)1);
        writer.Write(sampleRate); writer.Write(sampleRate * 2);
        writer.Write((short)2); writer.Write((short)16);
        writer.Write("data"u8); writer.Write(samples * 2);
        for (var i = 0; i < samples; i++) writer.Write((short)(8_000 * Math.Sin(i * 2 * Math.PI * 440 / sampleRate)));
    }
}
