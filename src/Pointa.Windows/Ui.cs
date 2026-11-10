using System.Drawing.Drawing2D;

namespace Pointa.Windows;

internal static class Ui
{
    public static readonly Color Ink = ColorTranslator.FromHtml("#172236");
    public static readonly Color Muted = ColorTranslator.FromHtml("#68758A");
    public static readonly Color Canvas = ColorTranslator.FromHtml("#F3F5F9");
    public static readonly Color Surface = Color.White;
    public static readonly Color Stroke = ColorTranslator.FromHtml("#DFE5EE");
    public static readonly Color Accent = ColorTranslator.FromHtml("#F87581");
    public static readonly Color Green = ColorTranslator.FromHtml("#16876B");

    public static Label Label(string text, float size = 9, bool bold = false) => new()
    {
        Text = text, AutoSize = false, AutoEllipsis = true,
        ForeColor = SystemInformation.HighContrast ? SystemColors.ControlText : Ink, BackColor = Color.Transparent,
        Font = new Font("Microsoft YaHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
        TextAlign = ContentAlignment.MiddleLeft,
    };

    public static Button Button(string text, EventHandler? action = null, bool primary = false)
    {
        var button = new ModernButton
        {
            Text = text, AccessibleName = text, AutoSize = false,
            BackColor = primary ? Ink : Surface, ForeColor = primary ? Color.White : Ink,
            FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand,
            Font = new Font("Microsoft YaHei UI", 9), UseVisualStyleBackColor = false,
        };
        button.FlatAppearance.BorderSize = 0;
        if (action is not null) button.Click += action;
        return button;
    }

    public static void Configure(Form form, string title, Size size)
    {
        form.SuspendLayout();
        form.Text = title;
        form.Font = new Font("Microsoft YaHei UI", 9);
        form.BackColor = SystemInformation.HighContrast ? SystemColors.Control : Canvas;
        form.ForeColor = Ink;
        form.ClientSize = size;
        form.StartPosition = FormStartPosition.CenterScreen;
        form.FormBorderStyle = FormBorderStyle.FixedSingle;
        form.MaximizeBox = false;
        form.Icon = CreateIcon();
        form.Disposed += (_, _) => form.Icon?.Dispose();
    }

    public static void Finish(Form form)
    {
        form.AutoScaleDimensions = new SizeF(96, 96);
        form.AutoScaleMode = AutoScaleMode.Dpi;
        form.ResumeLayout(true);
    }

    public static Icon CreateIcon(int size = 32)
    {
        using var stream = typeof(Ui).Assembly.GetManifestResourceStream("Pointa.Windows.Assets.Pointa.ico")
            ?? throw new InvalidOperationException("Pointa icon resource is missing.");
        using var icon = new Icon(stream, size, size);
        return (Icon)icon.Clone();
    }

    internal static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
    {
        var diameter = Math.Max(1, Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height)));
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class ModernButton : Button
{
    private bool hovered, pressed;
    public ModernButton() => DoubleBuffered = true;
    protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovered = pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { pressed = e.Button == MouseButtons.Left; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space) { pressed = true; Invalidate(); } base.OnKeyDown(e); }
    protected override void OnKeyUp(KeyEventArgs e) { pressed = false; Invalidate(); base.OnKeyUp(e); }
    protected override void OnLostFocus(EventArgs e) { pressed = false; Invalidate(); base.OnLostFocus(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width < 2 || Height < 2) return;
        var highContrast = SystemInformation.HighContrast;
        var background = highContrast ? SystemColors.Control : BackColor;
        var foreground = highContrast ? SystemColors.ControlText : Enabled ? ForeColor : Ui.Muted;
        e.Graphics.Clear(Parent?.BackColor ?? Ui.Canvas);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var shape = Ui.RoundedRectangle(new Rectangle(0, 0, Width - 1, Height - 1), 10 * DeviceDpi / 96);
        using var fill = new SolidBrush(background);
        e.Graphics.FillPath(fill, shape);
        if (Enabled && (hovered || pressed))
        {
            using var tint = new SolidBrush(Color.FromArgb(pressed ? 24 : 12, foreground));
            e.Graphics.FillPath(tint, shape);
        }
        using var border = new Pen(highContrast ? SystemColors.ControlText : BackColor == Ui.Ink ? Ui.Ink : Ui.Stroke);
        e.Graphics.DrawPath(border, shape);
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, foreground,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        if (Focused && ShowFocusCues)
        {
            var focus = ClientRectangle;
            focus.Inflate(-5 * DeviceDpi / 96, -5 * DeviceDpi / 96);
            ControlPaint.DrawFocusRectangle(e.Graphics, focus, foreground, background);
        }
    }
}

internal sealed class Card : Panel
{
    public Card() { DoubleBuffered = true; BackColor = SystemInformation.HighContrast ? SystemColors.Window : Ui.Surface; }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (Width < 2 || Height < 2) return;
        e.Graphics.Clear(Parent?.BackColor ?? Ui.Canvas);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Ui.RoundedRectangle(new Rectangle(0, 0, Width - 1, Height - 1), 16 * DeviceDpi / 96);
        using var fill = new SolidBrush(BackColor);
        e.Graphics.FillPath(fill, path);
        using var border = new Pen(SystemInformation.HighContrast ? SystemColors.WindowText : Ui.Stroke);
        e.Graphics.DrawPath(border, path);
    }
}
