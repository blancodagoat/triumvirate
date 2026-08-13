using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace Triumvirate;

/// <summary>
/// The hand-painted controls that make the window look designed instead of themed.
/// Stock WinForms chrome (TabControl especially) keeps its light system painting no
/// matter what colors you set, so the suite draws its own: rounded cards, a toggle
/// switch, pill buttons, and a sidebar nav.
/// </summary>
internal static class Ui
{
    /// <summary>
    /// Every size in the window is authored at 96 DPI and passed through here. The form
    /// is PerMonitorV2, so painted geometry has to follow <see cref="Control.DeviceDpi"/>
    /// the same way WinForms follows it for control bounds and fonts.
    /// </summary>
    public static int Dp(this Control control, int px) =>
        (int)Math.Round(px * control.DeviceDpi / 96.0, MidpointRounding.AwayFromZero);

    public static GraphicsPath Rounded(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        radius = Math.Min(radius, Math.Min(r.Width, r.Height) / 2);
        if (radius < 1)
        {
            path.AddRectangle(r);
            return path;
        }

        int d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>The paintable rectangle, one pixel short so the 1px edge lands inside.</summary>
    public static Rectangle Edge(Control c) => new(0, 0, c.Width - 1, c.Height - 1);
}

/// <summary>A rounded container on the window background.</summary>
internal sealed class Card : Panel
{
    public Card()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = Theme.Background;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width < 2 || Height < 2)
        {
            return;
        }

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Ui.Rounded(Ui.Edge(this), this.Dp(10));
        using var fill = new SolidBrush(Theme.Field);
        using var edge = new Pen(Theme.Border);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(edge, path);
        base.OnPaint(e);
    }
}

/// <summary>A pill button; Primary gets the accent, the rest stay quiet.</summary>
internal sealed class Pill : Control
{
    private bool hover;
    private bool pressed;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Primary { get; init; }

    public Pill(string text, int width = 150)
    {
        Text = text;
        // Transparent, so the rounded shape floats on whatever it sits on — an opaque
        // control rectangle read as a square frame around the pill on cards.
        SetStyle(ControlStyles.SupportsTransparentBackColor
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        ForeColor = Theme.Text;
        Font = Theme.Ui(9.5f);
        Size = new Size(this.Dp(width), this.Dp(32));
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Focus(); Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnEnabledChanged(EventArgs e)
    {
        // A pill that gets disabled mid-hover (every "Downloading…" button) would keep
        // the hover fill when it comes back otherwise.
        hover = false;
        pressed = false;
        Invalidate();
        base.OnEnabledChanged(e);
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Space or Keys.Enter || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter)
        {
            pressed = true;
            Invalidate();
            OnClick(EventArgs.Empty);
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e) { pressed = false; Invalidate(); base.OnKeyUp(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width < 2 || Height < 2)
        {
            return;
        }

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Color back;
        if (!Enabled)
        {
            back = Theme.Field;
        }
        else if (Primary)
        {
            back = pressed ? ControlPaint.Dark(Theme.Accent, 0.03f)
                : hover ? ControlPaint.Light(Theme.Accent, 0.1f)
                : Theme.Accent;
        }
        else
        {
            back = pressed ? Theme.Border : hover ? Theme.FieldHover : Theme.Field;
        }

        var r = Ui.Edge(this);
        using var path = Ui.Rounded(r, r.Height / 2);
        using var fill = new SolidBrush(back);
        using var edge = new Pen(Focused && Enabled
            ? (Primary ? Theme.Text : Theme.Accent)
            : Primary ? back : Theme.Border);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(edge, path);
        TextRenderer.DrawText(
            e.Graphics, Text, Font, ClientRectangle,
            !Enabled ? Theme.Dim : Primary ? Theme.Background : Theme.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}

/// <summary>A proper toggle switch: accent track when on, dim when off.</summary>
internal sealed class Toggle : Control
{
    private bool isOn;
    private bool hover;

    public event Action<bool>? Toggled;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool On
    {
        get => isOn;
        set
        {
            isOn = value;
            Invalidate();
        }
    }

    public Toggle()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        Size = new Size(this.Dp(44), this.Dp(24));
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { Focus(); base.OnMouseDown(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Space or Keys.Enter || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter)
        {
            OnClick(EventArgs.Empty);
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }

    protected override void OnClick(EventArgs e)
    {
        isOn = !isOn;
        Invalidate();
        Toggled?.Invoke(isOn);
        base.OnClick(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width < 2 || Height < 2)
        {
            return;
        }

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var r = Ui.Edge(this);
        var track = isOn
            ? hover ? ControlPaint.Light(Theme.Accent, 0.1f) : Theme.Accent
            : hover ? Theme.Border : Theme.FieldHover;

        using var shape = Ui.Rounded(r, r.Height / 2);
        using var fill = new SolidBrush(track);
        using var edge = new Pen(Focused ? Theme.Text : isOn ? track : Theme.Border);
        e.Graphics.FillPath(fill, shape);
        e.Graphics.DrawPath(edge, shape);

        // Proportional inset, so the knob keeps its optical weight at any scaling and
        // the float math lands it on the true centre rather than a rounded-off one.
        float inset = r.Height * 0.17f;
        float knob = r.Height - (inset * 2f);
        float x = isOn ? r.Right - inset - knob : r.X + inset;
        using var knobFill = new SolidBrush(isOn ? Theme.Background : Theme.Dim);
        e.Graphics.FillEllipse(knobFill, x, r.Y + inset, knob, knob);
    }
}

/// <summary>The left navigation: wordmark on top, one entry per page, accent bar on
/// the selection.</summary>
internal sealed class Sidebar : Panel
{
    private readonly List<string> items = [];
    private int selected;
    private int hovered = -1;

    public event Action<int>? Selected;

    public Sidebar(IEnumerable<string> entries)
    {
        items.AddRange(entries);
        Dock = DockStyle.Left;
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = Theme.Background;
        Width = this.Dp(168);
    }

    private int FirstRow => this.Dp(76);
    private int RowHeight => this.Dp(38);

    private int HitRow(Point p)
    {
        int row = (p.Y - FirstRow) / RowHeight;
        return p.Y >= FirstRow && row >= 0 && row < items.Count ? row : -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int row = HitRow(e.Location);
        if (row != hovered)
        {
            hovered = row;
            Invalidate();
        }

        Cursor = row >= 0 ? Cursors.Hand : Cursors.Default;
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        hovered = -1;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        int row = HitRow(e.Location);
        if (row >= 0 && row != selected)
        {
            selected = row;
            Invalidate();
            Selected?.Invoke(row);
        }

        base.OnMouseClick(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        using var line = new Pen(Theme.Border);
        e.Graphics.DrawLine(line, Width - 1, 0, Width - 1, Height);

        // Derived from the inherited font rather than a fresh point size: WinForms rescales
        // control fonts on a DPI change, so anything derived from them follows along.
        using var wordmark = new Font(Font.FontFamily, Font.SizeInPoints * 1.35f, FontStyle.Bold);
        TextRenderer.DrawText(
            e.Graphics, "Triumvirate", wordmark,
            new Rectangle(this.Dp(18), this.Dp(22), Width - this.Dp(28), this.Dp(26)), Theme.Accent,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        using var idle = new Font(Font, FontStyle.Regular);
        using var active = new Font(Font, FontStyle.Bold);
        int gutter = this.Dp(8);
        int textLeft = this.Dp(18);

        for (int i = 0; i < items.Count; i++)
        {
            var row = new Rectangle(
                gutter, FirstRow + (i * RowHeight),
                Width - 1 - (gutter * 2), RowHeight - this.Dp(6));

            if (i == selected || i == hovered)
            {
                using var path = Ui.Rounded(row, this.Dp(8));
                using var fill = new SolidBrush(i == selected ? Theme.Field : Theme.FieldHover);
                e.Graphics.FillPath(fill, path);
            }

            if (i == selected)
            {
                using var bar = new SolidBrush(Theme.Accent);
                e.Graphics.FillRectangle(
                    bar, row.X + this.Dp(5), row.Y + this.Dp(8),
                    this.Dp(3), row.Height - this.Dp(16));
            }

            TextRenderer.DrawText(
                e.Graphics, items[i], i == selected ? active : idle,
                new Rectangle(row.X + textLeft, row.Y, row.Width - textLeft - this.Dp(8), row.Height),
                i == selected ? Theme.Text : Theme.Dim,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }
}
