namespace TOOL_LOCAL.Projects;

public sealed class CreateProjectForm : Form
{
    private readonly TextBox _name = new();
    private readonly TextBox _topic = new();
    private readonly ComboBox _platform = new();
    private readonly ComboBox _aspectRatio = new();
    private readonly NumericUpDown _duration = new();
    private readonly NumericUpDown _budget = new();

    public CreateProjectCommand? Command { get; private set; }

    public CreateProjectForm()
    {
        Text = "Tạo video project";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(540, 500);

        ConfigureUi();
    }

    private void ConfigureUi()
    {
        ConfigureTextBox(_name, 30, 55, 480);
        ConfigureTextBox(_topic, 30, 125, 480);
        _topic.Multiline = true;
        _topic.Height = 100;

        _platform.Location = new Point(30, 265);
        _platform.Size = new Size(225, 28);
        _platform.DropDownStyle = ComboBoxStyle.DropDownList;
        _platform.Items.AddRange(["TikTok", "YouTubeShorts", "InstagramReels", "YouTube", "Facebook"]);
        _platform.SelectedIndex = 0;

        _aspectRatio.Location = new Point(285, 265);
        _aspectRatio.Size = new Size(225, 28);
        _aspectRatio.DropDownStyle = ComboBoxStyle.DropDownList;
        _aspectRatio.Items.AddRange(["9:16", "16:9", "1:1"]);
        _aspectRatio.SelectedIndex = 0;

        _duration.Location = new Point(30, 335);
        _duration.Size = new Size(225, 27);
        _duration.Minimum = 5;
        _duration.Maximum = 3600;
        _duration.Value = 45;

        _budget.Location = new Point(285, 335);
        _budget.Size = new Size(225, 27);
        _budget.DecimalPlaces = 2;
        _budget.Maximum = 100000;
        _budget.Value = 10;

        var createButton = new Button
        {
            Text = "Tạo project",
            Location = new Point(285, 420),
            Size = new Size(225, 42)
        };
        createButton.Click += CreateButtonOnClick;

        var cancelButton = new Button
        {
            Text = "Hủy",
            Location = new Point(30, 420),
            Size = new Size(225, 42),
            DialogResult = DialogResult.Cancel
        };

        Controls.AddRange([
            CreateLabel("Tên project", 30, 30),
            _name,
            CreateLabel("Chủ đề", 30, 100),
            _topic,
            CreateLabel("Nền tảng", 30, 240),
            _platform,
            CreateLabel("Tỷ lệ khung hình", 285, 240),
            _aspectRatio,
            CreateLabel("Thời lượng (giây)", 30, 310),
            _duration,
            CreateLabel("Ngân sách tối đa (USD)", 285, 310),
            _budget,
            cancelButton,
            createButton
        ]);

        AcceptButton = createButton;
        CancelButton = cancelButton;
    }

    private void CreateButtonOnClick(object? sender, EventArgs eventArgs)
    {
        if (string.IsNullOrWhiteSpace(_name.Text) || string.IsNullOrWhiteSpace(_topic.Text))
        {
            MessageBox.Show("Vui lòng nhập tên project và chủ đề.", "Thiếu dữ liệu", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Command = new CreateProjectCommand(
            _name.Text,
            _topic.Text,
            _platform.SelectedItem!.ToString()!,
            _aspectRatio.SelectedItem!.ToString()!,
            decimal.ToInt32(_duration.Value),
            _budget.Value > 0 ? _budget.Value : null);
        DialogResult = DialogResult.OK;
        Close();
    }

    private static Label CreateLabel(string text, int x, int y) =>
        new() { Text = text, AutoSize = true, Location = new Point(x, y) };

    private static void ConfigureTextBox(TextBox textBox, int x, int y, int width)
    {
        textBox.Location = new Point(x, y);
        textBox.Size = new Size(width, 27);
    }
}
