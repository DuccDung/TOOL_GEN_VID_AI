using System.Drawing.Drawing2D;
using System.ComponentModel;

namespace TOOL_LOCAL.Authentication;

internal static class AuthTheme
{
    public static readonly Color BackgroundTop = Color.FromArgb(250, 252, 255);
    public static readonly Color BackgroundBottom = Color.FromArgb(244, 248, 255);
    public static readonly Color Card = Color.White;
    public static readonly Color TextPrimary = Color.FromArgb(21, 35, 61);
    public static readonly Color TextSecondary = Color.FromArgb(112, 128, 154);
    public static readonly Color Border = Color.FromArgb(220, 228, 239);
    public static readonly Color BorderFocus = Color.FromArgb(52, 116, 235);
    public static readonly Color Primary = Color.FromArgb(46, 108, 232);
    public static readonly Color PrimaryLight = Color.FromArgb(76, 139, 246);
    public static readonly Color PrimaryDark = Color.FromArgb(35, 88, 202);
    public static readonly Color Decoration = Color.FromArgb(231, 239, 253);
    public static readonly Color Danger = Color.FromArgb(194, 54, 54);
    public static readonly Color Success = Color.FromArgb(31, 132, 90);

    public const string FontFamily = "Segoe UI";

    public static GraphicsPath RoundedRectangle(Rectangle rectangle, int radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Min(radius * 2, Math.Min(rectangle.Width, rectangle.Height));
        if (diameter <= 0)
        {
            path.AddRectangle(rectangle);
            return path;
        }

        var arc = new Rectangle(rectangle.Location, new Size(diameter, diameter));
        path.AddArc(arc, 180, 90);
        arc.X = rectangle.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = rectangle.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = rectangle.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class AuthBackgroundPanel : Panel
{
    public AuthBackgroundPanel()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);
    }

    protected override void OnPaintBackground(PaintEventArgs eventArgs)
    {
        var graphics = eventArgs.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var background = new LinearGradientBrush(
            ClientRectangle,
            AuthTheme.BackgroundTop,
            AuthTheme.BackgroundBottom,
            LinearGradientMode.Vertical);
        graphics.FillRectangle(background, ClientRectangle);

        using var decorationBrush = new SolidBrush(AuthTheme.Decoration);
        using var paleBrush = new SolidBrush(Color.FromArgb(150, 240, 245, 255));
        using var outlinePen = new Pen(Color.FromArgb(236, 241, 250), 5f);

        graphics.FillEllipse(decorationBrush, -88, -92, 225, 225);
        graphics.FillEllipse(paleBrush, Width - 72, 245, 170, 170);
        graphics.FillEllipse(decorationBrush, -96, Height - 115, 210, 210);
        graphics.FillEllipse(paleBrush, Width - 98, Height - 85, 190, 190);
        graphics.DrawEllipse(outlinePen, -17, 260, 43, 43);

        DrawDotGrid(graphics, new Point(20, 345), 4, 5);
        DrawDotGrid(graphics, new Point(Math.Max(0, Width - 54), 72), 4, 4);
        DrawDotGrid(graphics, new Point(Math.Max(0, Width - 47), Math.Max(0, Height - 185)), 4, 5);
    }

    private static void DrawDotGrid(Graphics graphics, Point origin, int columns, int rows)
    {
        using var brush = new SolidBrush(Color.FromArgb(211, 226, 250));
        const int spacing = 11;
        const int dotSize = 4;
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                graphics.FillEllipse(
                    brush,
                    origin.X + (column * spacing),
                    origin.Y + (row * spacing),
                    dotSize,
                    dotSize);
            }
        }
    }
}

internal sealed class AuthCardPanel : Panel
{
    public AuthCardPanel()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor |
            ControlStyles.UserPaint,
            true);
        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        var graphics = eventArgs.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var shadowRectangle = new Rectangle(10, 13, Width - 20, Height - 23);
        using (var shadowPath = AuthTheme.RoundedRectangle(shadowRectangle, 13))
        using (var shadowBrush = new SolidBrush(Color.FromArgb(24, 44, 69, 105)))
        {
            graphics.FillPath(shadowBrush, shadowPath);
        }

        var cardRectangle = new Rectangle(8, 8, Width - 20, Height - 23);
        using var cardPath = AuthTheme.RoundedRectangle(cardRectangle, 13);
        using var cardBrush = new SolidBrush(AuthTheme.Card);
        using var borderPen = new Pen(Color.FromArgb(229, 234, 243));
        graphics.FillPath(cardBrush, cardPath);
        graphics.DrawPath(borderPen, cardPath);

        base.OnPaint(eventArgs);
    }
}

internal sealed class BrandHeader : Control
{
    public BrandHeader()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor |
            ControlStyles.UserPaint,
            true);
        BackColor = Color.Transparent;
        Size = new Size(330, 82);
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        var graphics = eventArgs.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        const int contentWidth = 232;
        var startX = (Width - contentWidth) / 2;
        var iconRectangle = new Rectangle(startX, 6, 43, 34);
        using (var iconPath = AuthTheme.RoundedRectangle(iconRectangle, 7))
        using (var iconBrush = new LinearGradientBrush(
                   iconRectangle,
                   Color.FromArgb(104, 159, 250),
                   Color.FromArgb(52, 112, 232),
                   LinearGradientMode.ForwardDiagonal))
        {
            graphics.FillPath(iconBrush, iconPath);
        }

        using (var lensBrush = new SolidBrush(Color.FromArgb(66, 126, 237)))
        {
            var lens = new[]
            {
                new Point(iconRectangle.Right - 2, iconRectangle.Top + 10),
                new Point(iconRectangle.Right + 12, iconRectangle.Top + 5),
                new Point(iconRectangle.Right + 12, iconRectangle.Bottom - 5),
                new Point(iconRectangle.Right - 2, iconRectangle.Bottom - 10)
            };
            graphics.FillPolygon(lensBrush, lens);
        }

        using (var playBrush = new SolidBrush(Color.White))
        {
            var play = new[]
            {
                new Point(iconRectangle.Left + 16, iconRectangle.Top + 9),
                new Point(iconRectangle.Left + 16, iconRectangle.Bottom - 9),
                new Point(iconRectangle.Left + 29, iconRectangle.Top + 17)
            };
            graphics.FillPolygon(playBrush, play);
        }

        using var brandFont = new Font(AuthTheme.FontFamily, 17f, FontStyle.Bold, GraphicsUnit.Point);
        var textX = iconRectangle.Right + 21;
        TextRenderer.DrawText(
            graphics,
            "Video",
            brandFont,
            new Point(textX, 8),
            AuthTheme.TextPrimary,
            TextFormatFlags.NoPadding);
        var videoWidth = TextRenderer.MeasureText(
            graphics,
            "Video",
            brandFont,
            Size.Empty,
            TextFormatFlags.NoPadding).Width;
        TextRenderer.DrawText(
            graphics,
            "Maker",
            brandFont,
            new Point(textX + videoWidth, 8),
            AuthTheme.Primary,
            TextFormatFlags.NoPadding);

        using var sloganFont = new Font(AuthTheme.FontFamily, 9f, FontStyle.Regular, GraphicsUnit.Point);
        TextRenderer.DrawText(
            graphics,
            "Tự động tạo video bằng AI chỉ với vài bước",
            sloganFont,
            new Rectangle(0, 52, Width, 22),
            AuthTheme.TextSecondary,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.NoPadding);
    }
}

internal enum AuthFieldIcon
{
    User,
    Email,
    Lock
}

internal sealed class AuthTextBox : UserControl
{
    private readonly Label _titleLabel = new();
    private readonly TextBox _textBox = new();
    private readonly Label _errorLabel = new();
    private bool _focused;
    private bool _isPassword;
    private AuthFieldIcon _fieldIcon;

    public AuthTextBox()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor |
            ControlStyles.UserPaint,
            true);
        BackColor = Color.Transparent;
        Size = new Size(350, 78);
        TabStop = false;

        _titleLabel.AutoSize = true;
        _titleLabel.Font = new Font(AuthTheme.FontFamily, 8.8f, FontStyle.Bold);
        _titleLabel.ForeColor = AuthTheme.TextPrimary;
        _titleLabel.Location = new Point(0, 0);
        _titleLabel.BackColor = Color.Transparent;

        _textBox.BorderStyle = BorderStyle.None;
        _textBox.BackColor = Color.White;
        _textBox.Font = new Font(AuthTheme.FontFamily, 10f);
        _textBox.ForeColor = AuthTheme.TextPrimary;
        _textBox.Location = new Point(42, 35);
        _textBox.Enter += (_, _) =>
        {
            _focused = true;
            Invalidate();
        };
        _textBox.Leave += (_, _) =>
        {
            _focused = false;
            Invalidate();
        };
        _textBox.TextChanged += (_, eventArgs) =>
        {
            if (_errorLabel.Visible)
            {
                ClearError();
            }

            ValueChanged?.Invoke(this, eventArgs);
        };

        _errorLabel.AutoEllipsis = true;
        _errorLabel.Font = new Font(AuthTheme.FontFamily, 8f);
        _errorLabel.ForeColor = AuthTheme.Danger;
        _errorLabel.Location = new Point(2, 64);
        _errorLabel.Size = new Size(Width - 4, 15);
        _errorLabel.Visible = false;
        _errorLabel.BackColor = Color.Transparent;

        Controls.AddRange([_titleLabel, _textBox, _errorLabel]);
        Resize += (_, _) => LayoutTextBox();
    }

    public event EventHandler? ValueChanged;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string LabelText
    {
        get => _titleLabel.Text;
        set => _titleLabel.Text = value;
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string PlaceholderText
    {
        get => _textBox.PlaceholderText;
        set => _textBox.PlaceholderText = value;
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Value
    {
        get => _textBox.Text;
        set => _textBox.Text = value;
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public AuthFieldIcon FieldIcon
    {
        get => _fieldIcon;
        set
        {
            _fieldIcon = value;
            Invalidate();
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IsPassword
    {
        get => _isPassword;
        set
        {
            _isPassword = value;
            _textBox.UseSystemPasswordChar = value;
            LayoutTextBox();
            Invalidate();
        }
    }

    public void ShowError(string message)
    {
        _errorLabel.Text = message;
        _errorLabel.Visible = !string.IsNullOrWhiteSpace(message);
        Invalidate();
    }

    public void ClearError()
    {
        _errorLabel.Text = string.Empty;
        _errorLabel.Visible = false;
        Invalidate();
    }

    public void FocusInput() => _textBox.Focus();

    protected override void OnMouseDown(MouseEventArgs eventArgs)
    {
        base.OnMouseDown(eventArgs);
        if (_isPassword && EyeRectangle.Contains(eventArgs.Location))
        {
            var selectionStart = _textBox.SelectionStart;
            _textBox.UseSystemPasswordChar = !_textBox.UseSystemPasswordChar;
            _textBox.Focus();
            _textBox.SelectionStart = selectionStart;
            Invalidate();
            return;
        }

        if (InputRectangle.Contains(eventArgs.Location))
        {
            _textBox.Focus();
        }
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        base.OnPaint(eventArgs);
        var graphics = eventArgs.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var borderColor = _errorLabel.Visible
            ? AuthTheme.Danger
            : _focused
                ? AuthTheme.BorderFocus
                : AuthTheme.Border;
        using var inputPath = AuthTheme.RoundedRectangle(InputRectangle, 7);
        using var fillBrush = new SolidBrush(Color.White);
        using var borderPen = new Pen(borderColor, _focused || _errorLabel.Visible ? 1.5f : 1f);
        graphics.FillPath(fillBrush, inputPath);
        graphics.DrawPath(borderPen, inputPath);

        using var iconPen = new Pen(_focused ? AuthTheme.Primary : Color.FromArgb(139, 154, 179), 1.5f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        DrawFieldIcon(graphics, iconPen);
        if (_isPassword)
        {
            DrawEyeIcon(graphics, iconPen);
        }
    }

    private Rectangle InputRectangle => new(0, 22, Width - 1, 42);

    private Rectangle EyeRectangle => new(Width - 40, 23, 39, 40);

    private void LayoutTextBox()
    {
        var rightPadding = _isPassword ? 43 : 14;
        _textBox.Location = new Point(41, 34);
        _textBox.Width = Math.Max(40, Width - 41 - rightPadding);
        _errorLabel.Size = new Size(Math.Max(0, Width - 4), 15);
    }

    private void DrawFieldIcon(Graphics graphics, Pen pen)
    {
        switch (_fieldIcon)
        {
            case AuthFieldIcon.User:
                graphics.DrawEllipse(pen, 16, 32, 8, 8);
                graphics.DrawArc(pen, 12, 41, 16, 11, 195, 150);
                break;
            case AuthFieldIcon.Email:
                graphics.DrawRectangle(pen, 12, 34, 17, 13);
                graphics.DrawLine(pen, 13, 35, 20, 41);
                graphics.DrawLine(pen, 28, 35, 20, 41);
                break;
            case AuthFieldIcon.Lock:
                graphics.DrawRectangle(pen, 14, 38, 14, 11);
                graphics.DrawArc(pen, 16, 30, 10, 13, 180, 180);
                graphics.DrawLine(pen, 21, 42, 21, 45);
                break;
        }
    }

    private void DrawEyeIcon(Graphics graphics, Pen pen)
    {
        var eye = new Rectangle(Width - 29, 37, 16, 9);
        graphics.DrawArc(pen, eye, 200, 140);
        graphics.DrawArc(pen, eye, 20, 140);
        graphics.DrawEllipse(pen, Width - 23, 39, 4, 4);
    }
}

internal sealed class AuthButton : Button
{
    private bool _hovered;
    private bool _pressed;

    public AuthButton()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        FlatAppearance.BorderColor = AuthTheme.Card;
        FlatAppearance.MouseDownBackColor = AuthTheme.Card;
        FlatAppearance.MouseOverBackColor = AuthTheme.Card;
        UseVisualStyleBackColor = false;
        BackColor = AuthTheme.Card;
        Cursor = Cursors.Hand;
        Font = new Font(AuthTheme.FontFamily, 10f, FontStyle.Bold);
        Height = 44;
        TabStop = true;
        SizeChanged += (_, _) => UpdateRoundedRegion();
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Primary { get; set; } = true;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? LeadingGlyph { get; set; }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color GlyphColor { get; set; } = AuthTheme.Primary;

    protected override void OnMouseEnter(EventArgs eventArgs)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(eventArgs);
    }

    protected override void OnMouseLeave(EventArgs eventArgs)
    {
        _hovered = false;
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(eventArgs);
    }

    protected override void OnMouseDown(MouseEventArgs eventArgs)
    {
        _pressed = true;
        Invalidate();
        base.OnMouseDown(eventArgs);
    }

    protected override void OnMouseUp(MouseEventArgs eventArgs)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(eventArgs);
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        var graphics = eventArgs.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rectangle = new Rectangle(0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
        using (var backgroundBrush = new SolidBrush(AuthTheme.Card))
        {
            graphics.FillRectangle(backgroundBrush, ClientRectangle);
        }

        using var path = AuthTheme.RoundedRectangle(rectangle, 6);

        if (Primary)
        {
            var topColor = !Enabled
                ? Color.FromArgb(164, 184, 220)
                : _pressed
                    ? AuthTheme.PrimaryDark
                    : _hovered
                        ? Color.FromArgb(61, 126, 240)
                        : AuthTheme.PrimaryLight;
            var bottomColor = !Enabled
                ? Color.FromArgb(145, 168, 209)
                : _pressed
                    ? AuthTheme.PrimaryDark
                    : AuthTheme.Primary;
            using var brush = new LinearGradientBrush(rectangle, topColor, bottomColor, LinearGradientMode.Vertical);
            graphics.FillPath(brush, path);
        }
        else
        {
            using var brush = new SolidBrush(_hovered ? Color.FromArgb(247, 250, 255) : Color.White);
            using var borderPen = new Pen(_hovered ? Color.FromArgb(184, 205, 240) : AuthTheme.Border);
            graphics.FillPath(brush, path);
            graphics.DrawPath(borderPen, path);
        }

        var textColor = Primary ? Color.White : AuthTheme.TextPrimary;
        TextRenderer.DrawText(
            graphics,
            Text,
            Font,
            rectangle,
            Enabled ? textColor : Color.FromArgb(220, 225, 234),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

        if (!string.IsNullOrWhiteSpace(LeadingGlyph))
        {
            using var glyphFont = new Font(AuthTheme.FontFamily, 13f, FontStyle.Bold);
            TextRenderer.DrawText(
                graphics,
                LeadingGlyph,
                glyphFont,
                new Rectangle(16, 0, 28, Height),
                Enabled ? GlyphColor : Color.FromArgb(185, 193, 207),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }

    protected override void OnPaintBackground(PaintEventArgs eventArgs)
    {
        eventArgs.Graphics.Clear(AuthTheme.Card);
    }

    private void UpdateRoundedRegion()
    {
        if (Width <= 0 || Height <= 0)
        {
            return;
        }

        using var path = AuthTheme.RoundedRectangle(new Rectangle(0, 0, Width, Height), 6);
        var previousRegion = Region;
        Region = new Region(path);
        previousRegion?.Dispose();
    }
}

internal sealed class AuthDivider : Control
{
    public AuthDivider()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor |
            ControlStyles.UserPaint,
            true);
        BackColor = Color.Transparent;
        Height = 28;
        Text = "Hoặc tiếp tục với";
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        var graphics = eventArgs.Graphics;
        using var font = new Font(AuthTheme.FontFamily, 8.7f);
        var textSize = TextRenderer.MeasureText(graphics, Text, font, Size.Empty, TextFormatFlags.NoPadding);
        var textX = (Width - textSize.Width) / 2;
        var centerY = Height / 2;
        using var pen = new Pen(Color.FromArgb(225, 231, 240));
        graphics.DrawLine(pen, 0, centerY, Math.Max(0, textX - 14), centerY);
        graphics.DrawLine(pen, Math.Min(Width, textX + textSize.Width + 14), centerY, Width, centerY);
        TextRenderer.DrawText(
            graphics,
            Text,
            font,
            new Point(textX, centerY - (textSize.Height / 2)),
            AuthTheme.TextSecondary,
            TextFormatFlags.NoPadding);
    }
}

internal sealed class SecurityFooter : Control
{
    public SecurityFooter()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.SupportsTransparentBackColor |
            ControlStyles.UserPaint,
            true);
        BackColor = Color.Transparent;
        Size = new Size(330, 28);
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        var graphics = eventArgs.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var shield = new[]
        {
            new Point(16, 5),
            new Point(23, 8),
            new Point(22, 16),
            new Point(16, 22),
            new Point(10, 16),
            new Point(9, 8)
        };
        using var pen = new Pen(Color.FromArgb(157, 177, 207), 1.5f);
        graphics.DrawPolygon(pen, shield);
        graphics.DrawLines(pen, [new Point(13, 13), new Point(15, 16), new Point(20, 11)]);
        using var font = new Font(AuthTheme.FontFamily, 8.6f);
        TextRenderer.DrawText(
            graphics,
            "Thông tin của bạn được bảo mật an toàn.",
            font,
            new Rectangle(34, 2, Width - 34, Height - 2),
            AuthTheme.TextSecondary,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine);
    }
}
