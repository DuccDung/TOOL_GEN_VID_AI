using TOOL_SHARED.Contracts.Updates;

namespace TOOL_SETUP;

internal sealed class InstallerForm : Form
{
    private readonly ComboBox _versions = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 410 };
    private readonly TextBox _directory = new() { Width = 330 };
    private readonly CheckBox _desktopShortcut = new() { Text = "Tạo shortcut ngoài Desktop", Checked = true, AutoSize = true };
    private readonly CheckBox _launchAfter = new() { Text = "Chạy VideoMaker sau khi cài", Checked = true, AutoSize = true };
    private readonly ProgressBar _progress = new() { Width = 410, Height = 12 };
    private readonly Label _status = new() { Width = 410, Height = 42, Text = "Đang tải danh sách phiên bản..." };
    private readonly Button _install = new() { Text = "Cài đặt", Width = 120, Height = 38, Enabled = false };
    private readonly Button _browse = new() { Text = "Chọn...", Width = 70 };
    private LauncherInstallerService? _service;
    private IReadOnlyList<DesktopReleaseResponse> _releases = [];
    private bool _completed;
    private bool _busy;

    public InstallerForm()
    {
        Text = "VideoMaker Setup";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(500, 390);
        MinimumSize = new Size(516, 429);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        BackColor = Color.White;
        Font = new Font("Segoe UI", 9.5f);
        _directory.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "VideoMaker");

        var title = new Label { Text = "Cài đặt VideoMaker", Font = new Font("Segoe UI Semibold", 20f), AutoSize = true };
        var subtitle = new Label { Text = "Chọn phiên bản và thư mục cài đặt.", ForeColor = Color.DimGray, AutoSize = true };
        var versionLabel = new Label { Text = "Phiên bản", AutoSize = true };
        var directoryLabel = new Label { Text = "Thư mục cài đặt", AutoSize = true };
        var directoryPanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        directoryPanel.Controls.AddRange([_directory, _browse]);
        var actions = new FlowLayoutPanel { Width = 410, Height = 45, FlowDirection = FlowDirection.RightToLeft };
        actions.Controls.Add(_install);
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(40, 30, 40, 20) };
        layout.Controls.AddRange([title, subtitle, Spacer(10), versionLabel, _versions, Spacer(7), directoryLabel, directoryPanel, Spacer(7), _desktopShortcut, _launchAfter, Spacer(8), _progress, _status, actions]);
        Controls.Add(layout);
        Shown += async (_, _) => await LoadVersionsAsync();
        _browse.Click += BrowseOnClick;
        _install.Click += async (_, _) => { if (_completed) Close(); else await InstallAsync(); };
        FormClosing += (_, args) => { if (_busy) args.Cancel = true; };
    }

    private static Control Spacer(int height) => new Panel { Width = 1, Height = height };

    private async Task LoadVersionsAsync()
    {
        try
        {
            _service = new LauncherInstallerService(InstallerOptions.Load());
            _releases = await _service.GetVersionsAsync(CancellationToken.None);
            _versions.Items.Clear();
            foreach (var release in _releases) _versions.Items.Add($"{release.Version} (build {release.BuildNumber}) - {release.Channel}");
            if (_versions.Items.Count > 0) _versions.SelectedIndex = 0;
            _status.Text = _versions.Items.Count > 0 ? "Sẵn sàng cài đặt." : "Server chưa có release có package.";
            _install.Enabled = _versions.Items.Count > 0;
        }
        catch (Exception exception)
        {
            _status.Text = exception.Message;
            _status.ForeColor = Color.Firebrick;
        }
    }

    private void BrowseOnClick(object? sender, EventArgs args)
    {
        using var dialog = new FolderBrowserDialog { SelectedPath = _directory.Text, ShowNewFolderButton = true };
        if (dialog.ShowDialog(this) == DialogResult.OK) _directory.Text = dialog.SelectedPath;
    }

    private async Task InstallAsync()
    {
        if (_service is null || _versions.SelectedIndex < 0 || string.IsNullOrWhiteSpace(_directory.Text)) return;
        _install.Enabled = false;
        _busy = true;
        _browse.Enabled = false;
        _versions.Enabled = false;
        try
        {
            var progress = new Progress<DesktopUpdateProgress>(value => { _progress.Value = Math.Clamp(value.Percent, 0, 100); _status.Text = value.Message; });
            await _service.InstallAsync(_releases[_versions.SelectedIndex], _directory.Text, _desktopShortcut.Checked, _launchAfter.Checked, progress, CancellationToken.None);
            _install.Text = "Hoàn tất";
            _completed = true;
            _install.Enabled = true;
        }
        catch (Exception exception)
        {
            _status.Text = exception.Message;
            _status.ForeColor = Color.Firebrick;
            _install.Enabled = true;
            _browse.Enabled = true;
            _versions.Enabled = true;
        }
        finally
        {
            _busy = false;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _service?.Dispose();
        base.Dispose(disposing);
    }
}
