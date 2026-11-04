using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace Pointa.Windows;

internal static class Ui
{
    public static readonly Color Ink = ColorTranslator.FromHtml("#243145");
    public static readonly Color Muted = ColorTranslator.FromHtml("#7E8BA0");
    public static readonly Color Surface = ColorTranslator.FromHtml("#F5F8FB");
    public static readonly Color Accent = ColorTranslator.FromHtml("#F87581");
    public static readonly Color Green = ColorTranslator.FromHtml("#239B79");

    public static Label Label(string text, float size = 9, bool bold = false) => new()
    {
        Text = text, AutoSize = false, ForeColor = Ink, BackColor = Color.Transparent,
        Font = new Font("Microsoft YaHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
        TextAlign = ContentAlignment.MiddleLeft,
        AccessibleName = text,
    };

    public static Button Button(string text, EventHandler? action = null)
    {
        var button = new Button
        {
            Text = text, AccessibleName = text, AutoSize = false, BackColor = Surface,
            ForeColor = Ink, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand,
            Font = new Font("Microsoft YaHei UI", 8.5f), UseVisualStyleBackColor = false,
        };
        button.FlatAppearance.BorderColor = ColorTranslator.FromHtml("#E1E7F1");
        if (action is not null) button.Click += action;
        return button;
    }

    public static void Configure(Form form, string title, Size size)
    {
        form.SuspendLayout();
        form.Text = title;
        form.Font = new Font("Microsoft YaHei UI", 9);
        form.BackColor = Color.White;
        form.ForeColor = Ink;
        form.ClientSize = size;
        form.StartPosition = FormStartPosition.CenterScreen;
        form.FormBorderStyle = FormBorderStyle.FixedSingle;
        form.MaximizeBox = false;
        form.Icon = CreateIcon();
    }

    public static void Finish(Form form)
    {
        form.AutoScaleDimensions = new SizeF(96, 96);
        form.AutoScaleMode = AutoScaleMode.Dpi;
        form.ResumeLayout(true);
    }

    public static Icon CreateIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);
        using var dark = new SolidBrush(Ink);
        using var pink = new SolidBrush(Accent);
        using var white = new SolidBrush(Color.White);
        graphics.FillEllipse(dark, 0, 0, 31, 31);
        using var pen = new Pen(Accent, 2.5f);
        graphics.DrawArc(pen, 7, 7, 18, 18, 35, 280);
        graphics.FillEllipse(pink, 12, 12, 8, 8);
        graphics.FillEllipse(white, 15, 14, 2, 2);
        var handle = bitmap.GetHicon();
        try { return (Icon)Icon.FromHandle(handle).Clone(); }
        finally { DestroyIcon(handle); }
    }

    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr handle);
}

internal sealed class Card : Panel
{
    public Card() { DoubleBuffered = true; BackColor = Color.White; Padding = new Padding(12); }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = new GraphicsPath();
        var radius = Math.Max(12, 20 * DeviceDpi / 96);
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        path.AddArc(rect.Left, rect.Top, radius, radius, 180, 90);
        path.AddArc(rect.Right - radius, rect.Top, radius, radius, 270, 90);
        path.AddArc(rect.Right - radius, rect.Bottom - radius, radius, radius, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - radius, radius, radius, 90, 90);
        path.CloseFigure();
        using var fill = new SolidBrush(Ui.Surface);
        e.Graphics.FillPath(fill, path);
    }
}
