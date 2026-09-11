using System.Drawing.Drawing2D;

namespace VMod;

internal static class VTheme
{
    public static readonly Color Back = Color.FromArgb(13, 18, 22);
    public static readonly Color BackRaised = Color.FromArgb(20, 27, 32);
    public static readonly Color Card = Color.FromArgb(25, 33, 38);
    public static readonly Color CardHover = Color.FromArgb(31, 40, 46);
    public static readonly Color Border = Color.FromArgb(55, 67, 73);
    public static readonly Color Text = Color.FromArgb(238, 235, 224);
    public static readonly Color Muted = Color.FromArgb(151, 160, 161);
    public static readonly Color Amber = Color.FromArgb(226, 157, 63);
    public static readonly Color AmberBright = Color.FromArgb(246, 188, 91);
    public static readonly Color Teal = Color.FromArgb(58, 194, 186);
    public static readonly Color Green = Color.FromArgb(91, 190, 117);
    public static readonly Color Red = Color.FromArgb(211, 92, 78);
    public static readonly Color Purple = Color.FromArgb(148, 116, 214);

    public static Font Font(float points, FontStyle style = FontStyle.Regular, string family = "Segoe UI")
        => new(family, points * 96F / 72F, style, GraphicsUnit.Pixel);

    public static GraphicsPath Rounded(Rectangle rectangle, int radius)
    {
        int diameter = radius * 2;
        GraphicsPath path = new();
        path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal enum VButtonKind
{
    Primary,
    Secondary,
    Teal,
    Danger,
    Ghost
}

internal sealed class VButton : Button
{
    private bool _hover;
    public VButtonKind Kind { get; set; } = VButtonKind.Secondary;
    public int Radius { get; set; } = 10;

    public VButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        Font = VTheme.Font(9.5F, FontStyle.Bold, "Segoe UI Semibold");
        ForeColor = VTheme.Text;
        BackColor = Color.Transparent;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Rectangle rect = new(0, 0, Width - 1, Height - 1);
        (Color fill, Color border, Color text) = Palette();
        if (_hover && Enabled)
            fill = ControlPaint.Light(fill, .08f);
        if (!Enabled)
        {
            fill = Color.FromArgb(31, 36, 39);
            text = Color.FromArgb(91, 99, 102);
            border = Color.FromArgb(43, 48, 51);
        }
        using GraphicsPath path = VTheme.Rounded(rect, Radius);
        using SolidBrush brush = new(fill);
        using Pen pen = new(border, 1F);
        e.Graphics.FillPath(brush, path);
        e.Graphics.DrawPath(pen, path);
        TextRenderer.DrawText(e.Graphics, Text, Font, rect, text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private (Color fill, Color border, Color text) Palette() => Kind switch
    {
        VButtonKind.Primary => (Color.FromArgb(166, 100, 36), VTheme.Amber, Color.White),
        VButtonKind.Teal => (Color.FromArgb(26, 105, 104), VTheme.Teal, Color.White),
        VButtonKind.Danger => (Color.FromArgb(91, 43, 40), VTheme.Red, Color.FromArgb(255, 205, 196)),
        VButtonKind.Ghost => (Color.FromArgb(0, 0, 0, 0), VTheme.Border, VTheme.Muted),
        _ => (Color.FromArgb(34, 44, 50), Color.FromArgb(72, 84, 90), VTheme.Text)
    };
}

internal class RunePanel : Panel
{
    public int Radius { get; set; } = 14;
    public Color FillColor { get; set; } = VTheme.Card;
    public Color BorderColor { get; set; } = VTheme.Border;

    public RunePanel()
    {
        BackColor = Color.Transparent;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
    }

    protected override void OnPaintBackground(PaintEventArgs e) { }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Rectangle rect = new(0, 0, Width - 1, Height - 1);
        using GraphicsPath path = VTheme.Rounded(rect, Radius);
        using SolidBrush fill = new(FillColor);
        using Pen border = new(BorderColor);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(border, path);
        base.OnPaint(e);
    }
}

internal sealed class StatCard : RunePanel
{
    private readonly Label _number;
    private readonly Label _caption;
    private Color _accent;

    public StatCard(string caption, Color accent)
    {
        _accent = accent;
        Size = new Size(180, 88);
        _number = new Label
        {
            Text = "0",
            Font = VTheme.Font(24F, FontStyle.Bold, "Segoe UI Semibold"),
            ForeColor = accent,
            Location = new Point(18, 9),
            Size = new Size(140, 42),
            BackColor = Color.Transparent
        };
        _caption = new Label
        {
            Text = caption,
            Font = VTheme.Font(9.5F),
            ForeColor = VTheme.Muted,
            Location = new Point(20, 55),
            AutoSize = true,
            BackColor = Color.Transparent
        };
        Controls.Add(_number);
        Controls.Add(_caption);
    }

    public void SetValue(int value) => _number.Text = value.ToString();

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using SolidBrush glow = new(Color.FromArgb(90, _accent));
        e.Graphics.FillEllipse(glow, Width - 26, 15, 8, 8);
    }
}

internal sealed class TogglePill : Control
{
    private bool _isOn;
    private bool _hover;

    public bool IsOn
    {
        get => _isOn;
        set { _isOn = value; Invalidate(); }
    }

    public TogglePill()
    {
        Size = new Size(46, 25);
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Color track = Enabled ? (IsOn ? VTheme.Teal : Color.FromArgb(68, 76, 80)) : Color.FromArgb(42, 47, 49);
        if (_hover && Enabled) track = ControlPaint.Light(track, .08f);
        using GraphicsPath path = VTheme.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), Height / 2);
        using SolidBrush trackBrush = new(track);
        e.Graphics.FillPath(trackBrush, path);
        int x = IsOn ? Width - Height + 3 : 3;
        using SolidBrush knob = new(Enabled ? Color.WhiteSmoke : Color.Gray);
        e.Graphics.FillEllipse(knob, x, 3, Height - 6, Height - 6);
    }
}

internal sealed class ModCard : RunePanel
{
    private readonly Label _name;
    private readonly Label _description;
    private readonly Label _meta;
    private readonly Label _status;
    private readonly TogglePill _toggle;
    private readonly VButton _install;
    private readonly VButton _remove;
    private ModView _view;

    public event EventHandler? InstallRequested;
    public event EventHandler? ToggleRequested;
    public event EventHandler? RemoveRequested;

    public ModCard(ModView view)
    {
        _view = view;
        Height = 92;
        Margin = new Padding(0, 0, 0, 10);
        Padding = new Padding(0);
        FillColor = VTheme.Card;
        BorderColor = VTheme.Border;
        Radius = 12;

        _name = new Label { Location = new Point(72, 14), Size = new Size(400, 24), Font = VTheme.Font(11.5F, FontStyle.Bold, "Segoe UI Semibold"), ForeColor = VTheme.Text, BackColor = Color.Transparent };
        _description = new Label { Location = new Point(72, 40), Size = new Size(550, 21), Font = VTheme.Font(9F), ForeColor = VTheme.Muted, BackColor = Color.Transparent, AutoEllipsis = true };
        _meta = new Label { Location = new Point(72, 65), Size = new Size(420, 18), Font = VTheme.Font(8.5F), ForeColor = Color.FromArgb(114, 132, 136), BackColor = Color.Transparent };
        _status = new Label { Size = new Size(112, 27), TextAlign = ContentAlignment.MiddleCenter, Font = VTheme.Font(8.5F, FontStyle.Bold, "Segoe UI Semibold"), BackColor = Color.Transparent };
        _toggle = new TogglePill();
        _toggle.Click += (_, _) => ToggleRequested?.Invoke(this, EventArgs.Empty);
        _install = new VButton { Text = "Установить", Size = new Size(112, 34), Kind = VButtonKind.Teal, Radius = 9 };
        _install.Click += (_, _) => InstallRequested?.Invoke(this, EventArgs.Empty);
        _remove = new VButton { Text = "Удалить", Size = new Size(90, 34), Kind = VButtonKind.Ghost, Radius = 9 };
        _remove.Click += (_, _) => RemoveRequested?.Invoke(this, EventArgs.Empty);
        Controls.AddRange(new Control[] { _name, _description, _meta, _status, _toggle, _install, _remove });
        ApplyView(view);
    }

    public ModView View => _view;

    public void ApplyView(ModView view)
    {
        _view = view;
        _name.Text = view.Package.DisplayName;
        _description.Text = view.Package.Description;
        _meta.Text = view.Package.VersionLabel + "   •   " + (view.Package.IsCustom ? "Добавлен вручную" : "Встроенный набор");
        _toggle.IsOn = view.State == ModState.Installed;
        _toggle.Enabled = view.State is ModState.Installed or ModState.Disabled;
        _install.Visible = view.State is ModState.New or ModState.NotInstalled;
        _remove.Visible = view.State is ModState.Installed or ModState.Disabled;
        (_status.Text, _status.ForeColor) = view.State switch
        {
            ModState.Installed => ("УСТАНОВЛЕН", VTheme.Green),
            ModState.Disabled => ("ОТКЛЮЧЁН", VTheme.AmberBright),
            ModState.New => ("НОВЫЙ", VTheme.Purple),
            _ => ("НЕ УСТАНОВЛЕН", VTheme.Muted)
        };
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_status is null || _toggle is null || _install is null || _remove is null || _description is null)
            return;
        _status.Location = new Point(Width - 382, 18);
        _toggle.Location = new Point(Width - 244, 20);
        _install.Location = new Point(Width - 212, 48);
        _remove.Location = new Point(Width - 100, 48);
        _description.Width = Math.Max(230, Width - 480);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Color accent = _view.State switch
        {
            ModState.Installed => VTheme.Teal,
            ModState.Disabled => VTheme.Amber,
            ModState.New => VTheme.Purple,
            _ => VTheme.Border
        };
        using SolidBrush circle = new(Color.FromArgb(37, 47, 52));
        e.Graphics.FillEllipse(circle, 18, 20, 44, 44);
        using Pen rune = new(accent, 2F);
        e.Graphics.DrawLine(rune, 31, 31, 49, 53);
        e.Graphics.DrawLine(rune, 49, 31, 31, 53);
        e.Graphics.DrawLine(rune, 40, 27, 40, 57);
    }
}

internal sealed class DropZone : RunePanel
{
    private bool _active;
    public bool Active
    {
        get => _active;
        set { _active = value; Invalidate(); }
    }

    public DropZone()
    {
        AllowDrop = true;
        FillColor = Color.FromArgb(19, 38, 40);
        BorderColor = VTheme.Teal;
        Radius = 14;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        FillColor = Active ? Color.FromArgb(25, 58, 58) : Color.FromArgb(19, 38, 40);
        base.OnPaint(e);
        Rectangle iconRect = new(18, 19, 42, 42);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using Pen pen = new(VTheme.Teal, 2F);
        e.Graphics.DrawEllipse(pen, iconRect);
        e.Graphics.DrawLine(pen, 39, 29, 39, 51);
        e.Graphics.DrawLine(pen, 28, 40, 50, 40);
        using Font titleFont = VTheme.Font(10F, FontStyle.Bold, "Segoe UI Semibold");
        using Font detailFont = VTheme.Font(8.7F);
        TextRenderer.DrawText(e.Graphics, "Перетащите ZIP-моды сюда", titleFont, new Rectangle(72, 16, Width - 84, 25), VTheme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(e.Graphics, "Они сохранятся в библиотеке VMod", detailFont, new Rectangle(72, 42, Width - 84, 22), VTheme.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
    }
}
