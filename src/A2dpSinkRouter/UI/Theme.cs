using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace A2dpSinkRouter.UI;

internal static class Theme
{
    public static bool Dark { get; set; }
    public static Color Background => Dark ? Color.FromArgb(25, 25, 25) : Color.White;
    public static Color Surface => Background;
    public static Color Ink => Dark ? Color.FromArgb(235, 235, 235) : Color.FromArgb(30, 30, 30);
    public static Color Muted => Dark ? Color.FromArgb(170, 170, 170) : Color.FromArgb(100, 100, 100);
    public static Color Border => Dark ? Color.FromArgb(65, 65, 65) : Color.FromArgb(218, 218, 218);
    public static Color Blue => Ink;
    public static Color Green => Ink;
    public static Color Red => Dark ? Color.FromArgb(255, 165, 165) : Color.FromArgb(168, 40, 40);
    public static Color Hover => Dark ? Color.FromArgb(51, 51, 51) : Color.FromArgb(238, 238, 238);
    public static void Apply(Control root)
    {
        root.BackColor = Surface;
        root.ForeColor = root is Label label && !label.Font.Bold ? Muted : Ink;
        if (root is CheckBox check) { check.FlatStyle = FlatStyle.Flat; check.UseVisualStyleBackColor = false; }
        foreach (Control child in root.Controls) Apply(child);
        if (root is ToolStrip strip) ApplyMenu(strip.Items);
        root.Invalidate();
    }
    public static void ApplyMenu(ToolStripItemCollection items)
    {
        foreach (ToolStripItem item in items)
        {
            item.BackColor = Surface; item.ForeColor = Ink;
            if (item is ToolStripDropDownItem menu)
            {
                menu.DropDown.BackColor = Surface; menu.DropDown.ForeColor = Ink;
                menu.DropDown.Renderer = new FlatMenuRenderer(); ApplyMenu(menu.DropDownItems);
            }
        }
    }
}

internal sealed class SharpPictureBox : PictureBox
{
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        if (Image is not null) e.Graphics.DrawImage(Image, ClientRectangle);
    }
}

internal enum ButtonTone { Secondary, Primary, Quiet, Danger }
internal sealed class BufferedPanel : Panel
{
    public BufferedPanel() { DoubleBuffered = true; ResizeRedraw = true; }
}
internal sealed class BufferedTableLayoutPanel : TableLayoutPanel
{
    public BufferedTableLayoutPanel() { DoubleBuffered = true; }
}

internal sealed class FlatMenuRenderer : ToolStripProfessionalRenderer
{
    public FlatMenuRenderer() { RoundedEdges = false; }
    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        using var brush = new SolidBrush(e.Item.Selected || e.Item.Pressed ? Theme.Hover : Theme.Surface);
        e.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, e.Item.Size));
    }
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) { e.TextColor = Theme.Ink; base.OnRenderItemText(e); }
    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { using var b = new SolidBrush(Theme.Surface); e.Graphics.FillRectangle(b, e.AffectedBounds); }
    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) { e.ArrowColor = Theme.Ink; base.OnRenderArrow(e); }
    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e) { using var p = new Pen(Theme.Border); e.Graphics.DrawLine(p, 8, e.Item.Height / 2, e.Item.Width - 8, e.Item.Height / 2); }
    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        using var p = new Pen(Theme.Ink, 2);
        int x = e.ImageRectangle.X + 3, y = e.ImageRectangle.Y + e.ImageRectangle.Height / 2;
        e.Graphics.DrawLines(p, new Point[] { new(x, y), new(x + 4, y + 4), new(x + 11, y - 4) });
    }
    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(Theme.Border);
        if (e.ToolStrip is ToolStripDropDown) e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        else e.Graphics.DrawLine(pen, 0, e.ToolStrip.Height - 1, e.ToolStrip.Width, e.ToolStrip.Height - 1);
    }
}

internal sealed class AppButton : Button
{
    private bool _hover, _pressed;
    private ButtonTone _tone;
    [DefaultValue(ButtonTone.Secondary)]
    public ButtonTone Tone { get => _tone; set { if (_tone == value) return; _tone = value; Invalidate(); } }
    public AppButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
        AutoSize = true; Cursor = Cursors.Hand; Height = 38;
        Margin = new(0, 0, 8, 4);
    }
    public override Size GetPreferredSize(Size proposedSize) => new(TextRenderer.MeasureText(Text, Font).Width + 30, (int)(38 * DeviceDpi / 96f));
    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(Parent?.BackColor ?? Theme.Surface);
        Color fill = Tone switch
        {
            ButtonTone.Primary => Theme.Ink,
            _ => _hover || _pressed ? Theme.Hover : Theme.Surface
        };
        Color ink = Tone == ButtonTone.Primary ? Theme.Background : Tone == ButtonTone.Danger ? Theme.Red : Theme.Ink;
        if (!Enabled) { fill = Theme.Hover; ink = Theme.Muted; }
        using var brush = new SolidBrush(fill); e.Graphics.FillRectangle(brush, ClientRectangle);
        if (Tone == ButtonTone.Secondary)
        {
            using var pen = new Pen(Theme.Border); e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
        if (Focused && ShowFocusCues)
        {
            ControlPaint.DrawFocusRectangle(e.Graphics, new Rectangle(3, 3, Width - 6, Height - 6), ink, fill);
        }
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

internal sealed class SurfaceCard : Panel
{
    public TableLayoutPanel Content { get; } = new BufferedTableLayoutPanel { ColumnCount = 1, AutoSize = true, Dock = DockStyle.Top, Padding = new(8, 12, 8, 12), Margin = Padding.Empty, BackColor = Theme.Surface };
    private int _measuredWidth = -1, _measuredHeight;
    private Label[]? _wrappedLabels;
    public int MeasureCount { get; private set; }
    public SurfaceCard()
    {
        DoubleBuffered = true; ResizeRedraw = true; BackColor = Theme.Background; Padding = new(0, 12, 0, 8);
        Content.ColumnStyles.Add(new(SizeType.Percent, 100)); Controls.Add(Content);
    }
    public int MeasureHeight(int width)
    {
        if (_measuredWidth == width) return _measuredHeight;
        MeasureCount++;
        SuspendLayout(); Content.SuspendLayout();
        try
        {
            Width = width;
            int contentWidth = Math.Max(1, width - Padding.Horizontal);
            Content.Width = contentWidth;
            _wrappedLabels ??= Descendants(Content).OfType<Label>().Where(l => l.AutoSize).ToArray();
            var wrap = new Size(Math.Max(60, contentWidth - Content.Padding.Horizontal), 0);
            foreach (var label in _wrappedLabels) if (label.MaximumSize != wrap) label.MaximumSize = wrap;
            Content.ResumeLayout(true);
            _measuredHeight = Content.GetPreferredSize(new Size(contentWidth, 0)).Height + Padding.Vertical;
            _measuredWidth = width;
            return _measuredHeight;
        }
        finally { Content.ResumeLayout(false); ResumeLayout(true); }
    }
    public void InvalidateMeasure() => _measuredWidth = -1;
    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control c in root.Controls) { yield return c; foreach (var child in Descendants(c)) yield return child; }
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Theme.Border);
        e.Graphics.DrawLine(pen, 0, 0, Width, 0);
    }
}

internal sealed class StatusBadge : Control
{
    private Color _accent = Theme.Muted;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color Accent { get => _accent; set { if (_accent == value) return; _accent = value; Invalidate(); } }
    public StatusBadge() { DoubleBuffered = true; }
    protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Theme.Surface);
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}

internal sealed class LevelMeter : Control
{
    private int _value;
    [DefaultValue(0)]
    public int Value { get => _value; set { int next = Math.Clamp(value, 0, 1000); if (_value == next) return; _value = next; Invalidate(); } }
    public LevelMeter() { DoubleBuffered = true; Height = 8; Dock = DockStyle.Top; }
    protected override void OnPaint(PaintEventArgs e)
    {
        using var bg = new SolidBrush(Theme.Hover); e.Graphics.FillRectangle(bg, ClientRectangle);
        if (Value > 0)
        {
            using var fill = new SolidBrush(Theme.Ink);
            e.Graphics.FillRectangle(fill, 0, 0, Math.Max(2, Width * Value / 1000f), Height);
        }
    }
}

internal sealed class VolumeSlider : Control
{
    private int _value = 100;
    [DefaultValue(100)]
    public int Value { get => _value; set { int v = Math.Clamp(value, 0, 100); if (_value == v) return; _value = v; Invalidate(); ValueChanged?.Invoke(this, EventArgs.Empty); } }
    public event EventHandler? ValueChanged;
    public VolumeSlider() { DoubleBuffered = true; TabStop = true; Height = 32; Dock = DockStyle.Fill; AccessibleRole = AccessibleRole.Slider; Cursor = Cursors.Hand; }
    private void SetFromMouse(int x) => Value = (int)Math.Round(Math.Clamp((x - 9.0) / Math.Max(1, Width - 18), 0, 1) * 100);
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Focus(); Capture = true; SetFromMouse(e.X); }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (Capture) SetFromMouse(e.X); }
    protected override void OnMouseUp(MouseEventArgs e) { Capture = false; base.OnMouseUp(e); }
    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right or Keys.Home or Keys.End || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Left) Value -= 2; else if (e.KeyCode == Keys.Right) Value += 2;
        else if (e.KeyCode == Keys.Home) Value = 0; else if (e.KeyCode == Keys.End) Value = 100;
        base.OnKeyDown(e);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        float y = Height / 2f, x = 9 + (Width - 18) * Value / 100f;
        using var rail = new Pen(Theme.Border, 4) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var active = new Pen(Theme.Blue, 4) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        e.Graphics.DrawLine(rail, 9, y, Width - 9, y); e.Graphics.DrawLine(active, 9, y, x, y);
        using var knob = new SolidBrush(Theme.Surface); e.Graphics.FillEllipse(knob, x - 7, y - 7, 14, 14);
        using var edge = new Pen(Theme.Blue, Focused ? 3 : 2); e.Graphics.DrawEllipse(edge, x - 7, y - 7, 14, 14);
    }
}
