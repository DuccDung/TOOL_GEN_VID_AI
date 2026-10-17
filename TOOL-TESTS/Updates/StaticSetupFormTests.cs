extern alias SetupAssembly;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using SetupAssembly::TOOL_SETUP;
using TOOL_SHARED.Contracts.Updates;
using TOOL_SHARED.Distribution.StaticModels;
using Xunit;

namespace TOOL_TESTS.Updates;

public sealed class StaticSetupFormTests
{
    [Fact]
    public Task StandaloneOwnedTargetsClearlyOfferRepairOrUpdateWithoutRequiringReadyRuntime() => OnSta(async () =>
    {
        var root = Path.Combine(Path.GetTempPath(), "setup-action-" + Guid.NewGuid().ToString("N"));
        var metadata = Path.Combine(root, "metadata"); var hash = new string('a', 64);
        var repair = Path.Combine(root, "repair"); var update = Path.Combine(root, "update"); var fresh = Path.Combine(root, "fresh");
        try
        {
            WriteOwner(repair, "1.0.0", 7, hash); WriteOwner(update, "0.9.0", 6, new string('b', 64)); Directory.CreateDirectory(fresh);
            var release = new DesktopReleaseResponse(Guid.NewGuid(), "VideoMaker", "1.0.0", 7, "Stable", "win-x64", null, null,
                DateTime.UtcNow, "app.zip", "https://103.179.190.182:8443/apps/videomaker/app.zip", 100, hash,
                SourceKind: DesktopArtifactSourceKinds.ExternalVps, InstallMode: DesktopInstallModes.StaticArchive,
                DistributionProtocol: 2, ModelBundleId: ApprovedStaticModelBundle.Current.BundleId);
            using var session = new FixtureInstallerSession(releaseOverride: release);
            using var form = new InstallerForm(session, new InstallerOptions { ModelDeliveryMode = "StaticArchive" }, true,
                ownershipMetadataRoot: metadata);
            form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-20000, -20000); form.Show();
            var install = Field<Button>(form, "_install"); await PumpUntil(() => install.Enabled);
            var directory = Field<TextBox>(form, "_directory"); directory.Text = repair;
            Assert.Equal("Sửa chữa", install.Text); Assert.Contains("Sửa chữa", Field<Label>(form, "_bundle").Text);
            directory.Text = update; Assert.Equal("Cập nhật", install.Text);
            directory.Text = fresh; Assert.Equal("Cài đặt", install.Text);
            Assert.Equal(0, session.InstallCalls);
            Assert.False(File.Exists(Path.Combine(repair, "TOOL-LOCAL.exe"))); // repairing a non-starting app remains available
            Assert.False(File.Exists(Path.Combine(metadata, "static-model-install.json"))); form.Close();
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }

        void WriteOwner(string target, string version, int build, string packageHash)
        {
            Directory.CreateDirectory(target); var operation = Guid.NewGuid().ToString("N");
            Write(Path.Combine(target, "static-setup-receipt.json"), new { schemaVersion = 1, targetRoot = target,
                bundleId = ApprovedStaticModelBundle.Current.BundleId, version, buildNumber = build, packageSha256 = packageHash,
                operationId = operation, completedAtUtc = DateTime.UtcNow });
            Write(Path.Combine(metadata, "installer-transactions", StaticSetupTransaction.TargetKey(target), "transaction.json"),
                new { schemaVersion = 1, targetRoot = target, operationId = operation, releaseSha256 = packageHash, state = "Committed", changes = Array.Empty<object>() });
        }
        static void Write(string path, object value)
        { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web))); }
    });

    [Theory]
    [InlineData(1f)]
    [InlineData(1.25f)]
    [InlineData(1.5f)]
    [InlineData(2f)]
    public Task ActionsStayInsideClientAndLongStatusRemainsScrollableUnderLayoutScaling(float scale) => OnSta(async () =>
    {
        using var form = new InstallerForm(null, null, false);
        form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-20000, -20000);
        if (scale != 1f)
        {
            var fonts = Descendants(form).Prepend(form).Select(control => (control, font: control.Font)).ToArray();
            foreach (var (control, font) in fonts) control.Font = new Font(font.FontFamily, font.SizeInPoints * scale, font.Style);
            form.Scale(new SizeF(scale, scale));
        }
        var status = Field<TextBox>(form, "_status");
        status.Text = string.Concat(Enumerable.Repeat("Thiếu dung lượng. Bộ cài chưa hoàn tất; cache được giữ để thử lại. ", 50));
        form.Show(); await PumpUntil(() => form.Visible);
        foreach (var name in new[] { "_install", "_cancel" })
        {
            var control = Field<Button>(form, name);
            var bounds = new Rectangle(form.PointToClient(control.PointToScreen(Point.Empty)), control.Size);
            Assert.True(form.ClientRectangle.Contains(bounds), $"{name} outside client at scale {scale}: {bounds}");
        }
        Assert.True(status.ReadOnly); Assert.True(status.Multiline); Assert.Equal(ScrollBars.Vertical, status.ScrollBars);
        Assert.True(status.WordWrap); Assert.Contains("thử lại", status.Text); Assert.True(status.TabStop);
        Assert.NotEmpty(status.AccessibleName!); form.Close();
    });

    [Fact]
    public Task RapidInstallCancelRetryIgnoresOldProgressAndKeepsTheUiResponsive() => OnSta(async () =>
    {
        using var session = new FixtureInstallerSession();
        using var form = new InstallerForm(session, new InstallerOptions(), true);
        form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-20000, -20000); form.Show();
        var install = Field<Button>(form, "_install"); var cancel = Field<Button>(form, "_cancel");
        await PumpUntil(() => install.Enabled);
        for (var i = 0; i < 10; i++) install.PerformClick();
        await PumpUntil(() => session.Progress.Count == 1);
        Assert.False(Field<TextBox>(form, "_directory").Enabled); Assert.False(Field<ComboBox>(form, "_versions").Enabled);
        var oldProgress = session.Progress[0];
        oldProgress.Report(new("prerequisite", 26, "prerequisites")); oldProgress.Report(new("models", 30, "models"));
        await PumpUntil(() => Field<ProgressBar>(form, "_progress").Value == 30);
        cancel.PerformClick(); oldProgress.Report(new("complete", 100, "stale completion"));
        await PumpUntil(() => install.Enabled && install.Text == "Thử lại");
        Assert.Contains("chưa hoàn tất", Field<TextBox>(form, "_status").Text, StringComparison.OrdinalIgnoreCase);
        install.PerformClick(); await PumpUntil(() => session.Progress.Count == 2);
        oldProgress.Report(new("complete", 100, "OLD OPERATION"));
        session.Progress[1].Report(new("models", 30, "NEW OPERATION"));
        await PumpUntil(() => Field<TextBox>(form, "_status").Text == "NEW OPERATION");
        Assert.Equal(30, Field<ProgressBar>(form, "_progress").Value);
        form.Close(); Assert.False(form.IsDisposed); // closing while busy requests cancellation and waits for cleanup
        await PumpUntil(() => install.Enabled && install.Text == "Thử lại");
        Assert.Equal(2, session.InstallCalls); Assert.True(Field<TextBox>(form, "_directory").Enabled);
        form.Close();
    });

    [Fact]
    public Task ClosingDuringVersionLookupCancelsTheSingleOutstandingRequest() => OnSta(async () =>
    {
        using var session = new FixtureInstallerSession(holdVersions: true);
        using var form = new InstallerForm(session, new InstallerOptions(), true);
        form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-20000, -20000); form.Show();
        // Form.Shown is posted to the native message loop; Show() returning does not mean
        // the lookup has begun. Exercise closing an outstanding request, not closing before Shown.
        await PumpUntil(() => session.VersionCalls > 0);
        var install = Field<Button>(form, "_install"); for (var i = 0; i < 10; i++) install.PerformClick();
        Assert.Equal(1, session.VersionCalls); Assert.False(install.Enabled);
        form.Close(); await PumpUntil(() => session.VersionToken.IsCancellationRequested);
        Assert.True(form.IsDisposed);
    });

    private static T Field<T>(InstallerForm form, string name) => (T)typeof(InstallerForm).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
    private static IEnumerable<Control> Descendants(Control parent)
    { foreach (Control child in parent.Controls) { yield return child; foreach (var item in Descendants(child)) yield return item; } }
    private static async Task PumpUntil(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!condition()) { if (DateTime.UtcNow > until) throw new TimeoutException("Isolated WinForms fixture did not reach its expected state."); await Task.Delay(10); }
    }
    private static async Task OnSta(Func<Task> action)
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
                var task = action();
                while (!task.IsCompleted) { Application.DoEvents(); Thread.Sleep(5); }
                task.GetAwaiter().GetResult(); completed.SetResult();
            }
            catch (Exception error) { completed.SetException(error); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }
    private sealed class FixtureInstallerSession(bool holdVersions = false, DesktopReleaseResponse? releaseOverride = null) : IInstallerSession
    {
        private readonly ConcurrentQueue<IProgress<DesktopUpdateProgress>> _progress = new();
        public IReadOnlyList<IProgress<DesktopUpdateProgress>> Progress => _progress.ToArray();
        public int InstallCalls; public int VersionCalls; public CancellationToken VersionToken;
        public Func<string, CancellationToken, Task<bool>>? ConfirmResourceWarningAsync { get; set; }
        public async Task<IReadOnlyList<DesktopReleaseResponse>> GetVersionsAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref VersionCalls); VersionToken = cancellationToken;
            if (holdVersions) await Task.Delay(Timeout.Infinite, cancellationToken);
            return [releaseOverride ?? new(Guid.NewGuid(), "VideoMaker", "1.0.0", 1, "Stable", "win-x64", null, null, DateTime.UtcNow, "app.zip", "/app.zip", 10, new string('a', 64))];
        }
        public async Task InstallAsync(DesktopReleaseResponse release, string installDirectory, bool createDesktopShortcut,
            bool launchAfterInstall, IProgress<DesktopUpdateProgress>? progress, CancellationToken cancellationToken)
        { Interlocked.Increment(ref InstallCalls); _progress.Enqueue(progress!); await Task.Delay(Timeout.Infinite, cancellationToken); }
        public void Dispose() { }
    }
}
