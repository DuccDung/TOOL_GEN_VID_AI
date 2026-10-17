using TOOL_SHARED.Contracts.Updates;
using TOOL_LOCAL.Authentication;

namespace TOOL_SETUP;

internal sealed class InstallerForm : Form
{
    private readonly ComboBox _versions = new() { Name = "versions", DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, TabIndex = 0, AccessibleName = "Phiên bản ứng dụng" };
    private readonly TextBox _directory = new() { Name = "directory", Dock = DockStyle.Fill, TabIndex = 1, AccessibleName = "Thư mục cài đặt ứng dụng" };
    private readonly CheckBox _desktopShortcut = new() { Text = "Tạo shortcut ngoài Desktop", Checked = true, AutoSize = true, TabIndex = 3 };
    private readonly CheckBox _launchAfter = new() { Text = "Chạy VideoMaker sau khi cài", Checked = true, AutoSize = true, TabIndex = 4 };
    private readonly ProgressBar _progress = new() { Name = "progress", Dock = DockStyle.Fill, Height = 14, AccessibleName = "Tiến độ toàn bộ lượt cài đặt" };
    private readonly Label _phase = new() { Name = "phase", AutoSize = true, Text = "Đang chuẩn bị", Margin = new Padding(0, 0, 0, 6) };
    private readonly TextBox _status = new() { Name = "status", Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
        ScrollBars = ScrollBars.Vertical, WordWrap = true, BorderStyle = BorderStyle.FixedSingle, TabIndex = 5,
        Text = "Đang tải danh sách phiên bản...", AccessibleName = "Chi tiết trạng thái cài đặt", BackColor = SystemColors.Window };
    private readonly Button _install = new() { Name = "install", Text = "Cài đặt", AutoSize = true, MinimumSize = new Size(128, 40), Enabled = false, TabIndex = 7 };
    private readonly Button _browse = new() { Name = "browse", Text = "&Chọn...", AutoSize = true, MinimumSize = new Size(80, 30), TabIndex = 2 };
    private readonly Button _cancel = new() { Name = "cancel", Text = "&Hủy", AutoSize = true, MinimumSize = new Size(96, 40), Enabled = false, TabIndex = 6 };
    private readonly Label _bundle = new() { Name = "bundle", AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(0, 8, 0, 12) };
    private readonly Panel _fields = new() { Dock = DockStyle.Fill, AutoScroll = true, Margin = new Padding(0, 12, 0, 12), TabIndex = 0 };
    private readonly InstallerOptions? _providedOptions;
    private readonly MaintenanceInvocation? _maintenanceInvocation;
    private readonly string? _ownershipMetadataRoot;
    private bool _maintenancePinned;
    private bool _closeOnly;
    private IInstallerSession? _service;
    private sealed record ReleaseItem(DesktopReleaseResponse? Legacy, DesktopDirectReleaseResponse? Direct, DesktopStandaloneReleaseResponse? Standalone = null)
    {
        internal string Version => Standalone?.Version ?? Direct?.Version ?? Legacy!.Version;
        internal int Build => Standalone?.DeclaredBuildNumber ?? Direct?.DeclaredBuildNumber ?? Legacy!.BuildNumber;
        internal string Channel => Standalone?.Channel ?? Direct?.Channel ?? Legacy!.Channel;
    }
    private IReadOnlyList<ReleaseItem> _releases = [];
    private bool _completed;
    private bool _busy;
    private bool _loadingVersions;
    private CancellationTokenSource? _installCancellation;
    private CancellationTokenSource? _loadCancellation;
    private int _displayedPercent;
    private int _operationGeneration;

    public InstallerForm() : this(null, null, true) { }

    internal InstallerForm(IInstallerSession? service, InstallerOptions? options, bool loadVersionsOnShown,
        MaintenanceInvocation? maintenanceInvocation = null, string? ownershipMetadataRoot = null)
    {
        _service = service;
        _providedOptions = options;
        _maintenanceInvocation = maintenanceInvocation;
        _ownershipMetadataRoot = ownershipMetadataRoot;
        BrandIdentity.ApplyWindowIcon(this);
        Text = "VideoMaker Setup";
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9.5f);
        ClientSize = new Size(560, 660);
        MinimumSize = new Size(480, 440);
        MaximizeBox = false;
        BackColor = SystemColors.Window;
        _directory.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "VideoMaker");
        var title = new Label { Text = "Cài đặt VideoMaker", Font = new Font("Segoe UI Semibold", 20f), AutoSize = true, Margin = new Padding(0, 0, 0, 4) };
        var subtitle = new Label { Text = "Chọn phiên bản và thư mục cài đặt.", AutoSize = true, ForeColor = SystemColors.GrayText, Margin = Padding.Empty };
        var header = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };
        header.Controls.AddRange([title, subtitle]);
        var inputs = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Margin = Padding.Empty, Padding = new Padding(0, 0, 4, 0) };
        inputs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var directoryPanel = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 4, 0, 12) };
        directoryPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        directoryPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        directoryPanel.Controls.Add(_directory, 0, 0); directoryPanel.Controls.Add(_browse, 1, 0);
        _browse.Margin = new Padding(8, 0, 0, 0); _directory.Margin = Padding.Empty;
        inputs.Controls.Add(new Label { Text = "Phiên bản", AutoSize = true, Margin = new Padding(0, 0, 0, 4) });
        inputs.Controls.Add(_versions); inputs.Controls.Add(_bundle);
        inputs.Controls.Add(new Label { Text = "Thư mục cài đặt", AutoSize = true, Margin = Padding.Empty });
        inputs.Controls.Add(directoryPanel); inputs.Controls.Add(_desktopShortcut); inputs.Controls.Add(_launchAfter);
        _fields.Controls.Add(inputs);
        _fields.ClientSizeChanged += (_, _) =>
        {
            var width = Math.Max(120, _fields.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 8);
            _bundle.MaximumSize = new Size(width, 0); subtitle.MaximumSize = new Size(width, 0);
        };
        var progressPanel = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, Margin = new Padding(0, 0, 0, 8) };
        progressPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        progressPanel.Controls.Add(_phase); progressPanel.Controls.Add(_progress);
        var actions = new FlowLayoutPanel { Name = "actions", Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = new Padding(0, 10, 0, 0), TabIndex = 3 };
        actions.Controls.Add(_install); actions.Controls.Add(_cancel);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(24, 20, 24, 18) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 110));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(header, 0, 0); layout.Controls.Add(_fields, 0, 1); layout.Controls.Add(progressPanel, 0, 2); layout.Controls.Add(_status, 0, 3); layout.Controls.Add(actions, 0, 4);
        Controls.Add(layout);
        Shown += async (_, _) => { FitWorkingArea(); if (_maintenanceInvocation is not null) await ReceiveMaintenanceAsync(); else if (loadVersionsOnShown) await LoadVersionsAsync(); };
        _browse.Click += BrowseOnClick;
        _directory.TextChanged += (_, _) => RefreshTargetAction();
        _versions.SelectedIndexChanged += (_, _) => RefreshTargetAction();
        _install.Click += async (_, _) => { if (_busy || _loadingVersions) return; if (_completed || _closeOnly) Close(); else if (_releases.Count == 0) await LoadVersionsAsync(); else await InstallAsync(); };
        _cancel.Click += (_, _) => CancelInstall();
        FormClosing += (_, args) => { if (_busy) { args.Cancel = true; CancelInstall(); } else _loadCancellation?.Cancel(); };
    }

    private async Task ReceiveMaintenanceAsync()
    {
        _loadingVersions = true; _install.Enabled = false; _cancel.Enabled = true;
        SetInputsEnabled(false); _phase.Text = "Đang xác minh chuyển giao";
        SetStatus("Đang đối chiếu bộ cài và bản ứng dụng hiện tại. Chưa thay file ứng dụng.");
        using var cancellation = new CancellationTokenSource(); _loadCancellation = cancellation;
        try
        {
            var options = _providedOptions ?? InstallerOptions.Load();
            _service ??= new LauncherInstallerService(options); _service.ConfirmResourceWarningAsync = ConfirmResourcesAsync;
            var prepared = await SetupMaintenanceHandoff.AcceptAsync(_maintenanceInvocation!, options, _service, cancellation.Token);
            var item = new ReleaseItem(prepared.Release, prepared.DirectRelease, prepared.StandaloneRelease);
            _maintenancePinned = true; _releases = [item]; _versions.Items.Clear();
            _versions.Items.Add($"{item.Version} (build {item.Build}) - {item.Channel}");
            _versions.SelectedIndex = 0; _directory.Text = prepared.TargetRoot;
            _launchAfter.Checked = prepared.LaunchAfterInstall; _desktopShortcut.Checked = false;
            _bundle.Text = "Đúng bộ cài của lượt update/repair. Tiếp tục kiểm tra đủ ứng dụng và model trước khi hoàn tất.";
            _phase.Text = "Đang chờ desktop đóng";
            SetStatus("Đã xác nhận chuyển giao. Đang chờ desktop đóng và nhả runtime; bộ cài không tự tắt ứng dụng.");
            await prepared.WaitForParentExitAsync(cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            _loadingVersions = false; _loadCancellation = null; _cancel.Enabled = false;
            await InstallAsync();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (IsDisposed || Disposing) return;
            _phase.Text = "Đã hủy chuyển giao"; SetStatus("Chưa hoàn tất lượt cài. Ứng dụng/model chưa được công nhận sẵn sàng từ lượt này.");
            _closeOnly = true; _install.Text = "Đóng";
        }
        catch (Exception error)
        {
            if (IsDisposed || Disposing) return;
            _phase.Text = "Chưa thể chuyển giao"; SetStatus(error.Message, true); _closeOnly = true; _install.Text = "Đóng";
        }
        finally
        {
            if (ReferenceEquals(_loadCancellation, cancellation)) _loadCancellation = null;
            _loadingVersions = false;
            if (!IsDisposed && !Disposing) { _cancel.Enabled = false; _install.Enabled = true; }
        }
    }

    private void FitWorkingArea()
    {
        var area = Screen.FromControl(this).WorkingArea;
        var available = new Size(Math.Max(320, area.Width - 24), Math.Max(300, area.Height - 24));
        MinimumSize = new Size(Math.Min(MinimumSize.Width, available.Width), Math.Min(MinimumSize.Height, available.Height));
        Size = new Size(Math.Min(Width, available.Width), Math.Min(Height, available.Height));
    }

    private async Task LoadVersionsAsync()
    {
        if (_loadingVersions || _busy) return;
        _loadingVersions = true; _install.Enabled = false; _loadCancellation = new CancellationTokenSource();
        try
        {
            var options = _providedOptions ?? InstallerOptions.Load();
            _bundle.Text = options.IsStaticArchive ? "Kèm Qwen 4B và Piper offline từ VPS (2,85 GB). Chỉ hoàn tất khi kiểm tra đủ model, OCR, media và WebView2." : string.Empty;
            _service ??= new LauncherInstallerService(options);
            _service.ConfirmResourceWarningAsync = ConfirmResourcesAsync;
            var standalone = await _service.GetStandaloneVersionsAsync(_loadCancellation.Token);
            IReadOnlyList<DesktopDirectReleaseResponse> direct = standalone.Count > 0 ? [] : await _service.GetDirectVersionsAsync(_loadCancellation.Token);
            IReadOnlyList<DesktopReleaseResponse> releases = standalone.Count > 0 ? [] : await _service.GetVersionsAsync(_loadCancellation.Token);
            if (IsDisposed || Disposing) return;
            _releases = standalone.Select(x => new ReleaseItem(null, null, x)).Concat(direct.Select(x => new ReleaseItem(null, x))).Concat(releases.Select(x => new ReleaseItem(x, null)))
                .OrderByDescending(x => System.Version.TryParse(x.Version, out var parsed) ? parsed : new Version(0, 0))
                .ThenByDescending(x => x.Build).ToArray(); _versions.Items.Clear();
            foreach (var release in _releases) _versions.Items.Add($"{release.Version} (build {release.Build}) - {release.Channel}");
            if (_versions.Items.Count > 0) _versions.SelectedIndex = 0;
            _phase.Text = _versions.Items.Count > 0 ? "Sẵn sàng cài đặt" : "Chưa có bản phát hành phù hợp";
            SetStatus(_versions.Items.Count > 0 ? "Sẵn sàng cài đặt." : "Server chưa có package khớp bản Setup. Hãy kiểm tra phát hành rồi thử lại.");
            _install.Text = _versions.Items.Count > 0 ? "Cài đặt" : "Thử lại kết nối";
        }
        catch (OperationCanceledException) when (IsDisposed || Disposing || _loadCancellation.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (IsDisposed || Disposing) return;
            _phase.Text = "Chưa kết nối được"; SetStatus(exception.Message, true); _install.Text = "Thử lại kết nối";
        }
        finally
        {
            _loadCancellation.Dispose(); _loadCancellation = null; _loadingVersions = false;
            if (!IsDisposed && !Disposing) { _install.Enabled = true; RefreshTargetAction(); }
        }
    }

    private void RefreshTargetAction()
    {
        if (_busy || _loadingVersions || _completed || _closeOnly || _maintenancePinned
            || _versions.SelectedIndex < 0 || _versions.SelectedIndex >= _releases.Count) return;
        var release = _releases[_versions.SelectedIndex];
        if (release.Standalone is null && release.Direct is null && release.Legacy!.InstallMode != DesktopInstallModes.StaticArchive) return;
        var action = release.Standalone is { } standalone
            ? LauncherInstallerService.DescribeInstallationAction(_directory.Text, standalone, _ownershipMetadataRoot)
            : release.Direct is { } direct
            ? LauncherInstallerService.DescribeInstallationAction(_directory.Text, direct, _ownershipMetadataRoot)
            : LauncherInstallerService.DescribeInstallationAction(_directory.Text, release.Legacy!, _ownershipMetadataRoot);
        _install.Text = action;
        _bundle.Text = action switch
        {
            "Sửa chữa" => "Đã nhận diện đúng bản cài này. Sửa chữa sẽ kiểm tra lại ứng dụng, model, OCR, media và WebView2 trước khi hoàn tất.",
            "Cập nhật" => "Đã nhận diện bản cài cũ. Cập nhật sẽ kiểm tra lại ứng dụng và bộ model trước khi hoàn tất.",
            _ => "Kèm Qwen 4B và Piper offline từ VPS (2,85 GB). Chỉ hoàn tất khi kiểm tra đủ model, OCR, media và WebView2."
        };
    }

    private Task<bool> ConfirmResourcesAsync(string message, CancellationToken token)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (IsDisposed || Disposing || token.IsCancellationRequested) return Task.FromResult(false);
        BeginInvoke((Action)(() =>
        {
            if (IsDisposed || Disposing || token.IsCancellationRequested) { completion.TrySetResult(false); return; }
            completion.TrySetResult(MessageBox.Show(this, message + "\n\nTiếp tục chạy kiểm tra model trên máy này?", "Xác nhận tài nguyên Qwen",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes);
        }));
        return completion.Task;
    }

    private void BrowseOnClick(object? sender, EventArgs args)
    {
        using var dialog = new FolderBrowserDialog { SelectedPath = _directory.Text, ShowNewFolderButton = true };
        if (dialog.ShowDialog(this) == DialogResult.OK) _directory.Text = dialog.SelectedPath;
    }

    private async Task InstallAsync()
    {
        if (_busy || _loadingVersions || _service is null || _versions.SelectedIndex < 0 || string.IsNullOrWhiteSpace(_directory.Text)) return;
        var session = _service; var release = _releases[_versions.SelectedIndex]; var directory = _directory.Text;
        var desktopShortcut = _desktopShortcut.Checked; var launch = _launchAfter.Checked;
        _busy = true; _displayedPercent = 0; _progress.Value = 0;
        var generation = ++_operationGeneration;
        _installCancellation = new CancellationTokenSource(); _cancel.Enabled = true; SetInputsEnabled(false); _install.Enabled = false;
        SetStatus("Đang chuẩn bị lượt cài đặt.");
        try
        {
            var progress = new Progress<DesktopUpdateProgress>(value => { if (generation == _operationGeneration) ReportProgress(value); });
            await Task.Run(() => release.Standalone is { } standalone
                ? session.InstallStandaloneAsync(standalone, directory, desktopShortcut, launch, progress, _installCancellation.Token)
                : release.Direct is { } direct
                ? session.InstallDirectAsync(direct, directory, desktopShortcut, launch, progress, _installCancellation.Token)
                : session.InstallAsync(release.Legacy!, directory, desktopShortcut, launch, progress, _installCancellation.Token));
            _install.Text = "Hoàn tất"; _completed = true; _install.Enabled = true; _phase.Text = "Đã hoàn tất";
            _displayedPercent = 100; _progress.Value = 100;
        }
        catch (OperationCanceledException)
        {
            _phase.Text = "Chưa hoàn tất — đã hủy"; _install.Text = "Thử lại";
            SetStatus("Đã hủy; cache và nhật ký được giữ. Bộ cài chưa hoàn tất, model cần được kiểm tra lại. Bấm Thử lại để tiếp tục.");
        }
        catch (Exception exception) { _phase.Text = "Chưa hoàn tất — cần xử lý"; SetStatus(exception.Message, true); _install.Text = "Thử lại"; }
        finally
        {
            _busy = false; _installCancellation.Dispose(); _installCancellation = null;
            ++_operationGeneration;
            _cancel.Enabled = false; _install.Enabled = true; SetInputsEnabled(!_completed);
        }
    }

    private void SetInputsEnabled(bool enabled)
    { _browse.Enabled = enabled && !_maintenancePinned; _versions.Enabled = enabled && !_maintenancePinned; _directory.Enabled = enabled && !_maintenancePinned; _desktopShortcut.Enabled = enabled; _launchAfter.Enabled = enabled; }

    internal void ReportProgress(DesktopUpdateProgress value)
    {
        if (!_busy || IsDisposed || Disposing || _installCancellation?.IsCancellationRequested == true) return;
        var completed = value.Stage == "complete" && value.Percent == 100;
        _displayedPercent = Math.Max(_displayedPercent, Math.Clamp(value.Percent, 0, completed ? 100 : 99));
        _progress.Value = _displayedPercent;
        _phase.Text = $"{SetupProgressStages.DisplayName(value.Stage)} · {_displayedPercent}%";
        SetStatus(value.Message);
    }

    private void SetStatus(string message, bool error = false)
    { _status.ForeColor = error ? Color.Firebrick : SystemColors.WindowText; _status.Text = message; _status.SelectionStart = 0; _status.SelectionLength = 0; }

    private void CancelInstall()
    {
        if (_installCancellation is null && _loadCancellation is not null) { _loadCancellation.Cancel(); _cancel.Enabled = false; return; }
        if (_installCancellation is null || _installCancellation.IsCancellationRequested) return;
        _installCancellation.Cancel(); _cancel.Enabled = false; _phase.Text = "Đang hủy";
        SetStatus("Đang hủy và khôi phục file ứng dụng. Vui lòng giữ cửa sổ mở.");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _loadCancellation?.Cancel(); _service?.Dispose(); }
        base.Dispose(disposing);
    }
}
