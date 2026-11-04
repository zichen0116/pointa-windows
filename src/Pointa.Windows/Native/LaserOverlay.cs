using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Pointa.Windows.Native;

public sealed class LaserOverlay : Form
{
    private const int MaximumTrailPoints = 128;
    private readonly int _uiThread = Environment.CurrentManagedThreadId;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 16 };
    private readonly List<TrailPoint> _trail = new(MaximumTrailPoints);
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private Screen _screen;
    private LayeredSurface? _surface;
    private PointF _target, _display;
    private bool _active, _enabled = true;
    private double _lastFrame;
    private Color _color = Color.FromArgb(255, 100, 117);
    private int _size = 16, _trailMs = 500;
    private string _smoothing = "standard";

    public string SelectedScreenName => _screen.DeviceName;
    public event EventHandler? ScreenChanged;
    public event EventHandler<string>? RenderingFailed;
    internal bool IsAnimationRunning => _timer.Enabled;

    public LaserOverlay()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        TopMost = true;
        Size = new Size(1, 1);
        Text = "Pointa Laser Overlay";
        _screen = Screen.PrimaryScreen ?? Screen.AllScreens[0];
        Location = _screen.Bounds.Location;
        _ = Handle;
        _timer.Tick += (_, _) => RenderFrame();
        SystemEvents.DisplaySettingsChanged += DisplaySettingsChanged;
    }

    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= 0x00080000 | 0x00000020 | 0x08000000 | 0x00000080;
            return parameters;
        }
    }

    public void MovePointer(double x, double y)
    {
        EnsureUiThread();
        if (!_enabled || !double.IsFinite(x) || !double.IsFinite(y) || x < 0 || x > 1 || y < 0 || y > 1) return;
        var next = MapPoint(_screen.Bounds, x, y);
        if (_active && next == _target) return;
        _target = next;
        if (!_active)
        {
            _display = _target;
            _trail.Clear();
            _lastFrame = _clock.Elapsed.TotalMilliseconds;
            if (_trailMs > 0) AddTrailPoint(_display, _lastFrame);
        }
        _active = true;
        if (!_timer.Enabled) RenderFrame();
    }

    public void HidePointer(bool immediate = false)
    {
        EnsureUiThread();
        if (_active && !immediate && _trailMs > 0)
            AddTrailPoint(_display, _clock.Elapsed.TotalMilliseconds);
        _active = false;
        if (immediate || _trailMs == 0 || _trail.Count == 0) StopAndHide();
        else _timer.Start();
    }

    public void SetEnabled(bool enabled)
    {
        EnsureUiThread();
        _enabled = enabled;
        if (!enabled) HidePointer(true);
    }

    public void UpdateEffect(string color, int size, int trailMs, string smoothing)
    {
        EnsureUiThread();
        var parsedColor = ParseColor(color);
        if (size is < 4 or > 48) throw new ArgumentOutOfRangeException(nameof(size));
        if (trailMs is < 0 or > 1200) throw new ArgumentOutOfRangeException(nameof(trailMs));
        var parsedSmoothing = smoothing switch
        {
            "low" or "低" => "low", "standard" or "标准" => "standard", "high" or "高" => "high",
            _ => throw new ArgumentException("Smoothing must be low, standard or high.", nameof(smoothing))
        };
        _color = parsedColor;
        _size = size;
        _trailMs = trailMs;
        _smoothing = parsedSmoothing;
        if (trailMs == 0) _trail.Clear();
        if (_active || _trail.Count > 0) RenderFrame();
        else StopAndHide();
    }

    public void SelectScreen(string? deviceName)
    {
        EnsureUiThread();
        var screens = Screen.AllScreens;
        var selected = screens.FirstOrDefault(screen => screen.DeviceName.Equals(deviceName, StringComparison.OrdinalIgnoreCase))
            ?? Screen.PrimaryScreen ?? screens[0];
        var changed = selected.DeviceName != _screen.DeviceName || selected.Bounds != _screen.Bounds;
        _screen = selected;
        HidePointer(true);
        // Moving the hidden HWND first obtains the destination monitor's DPI before sizing the pointer.
        if (!OverlayNative.SetWindowPos(Handle, 0, selected.Bounds.Left, selected.Bounds.Top, 1, 1, 0x0014))
            RenderingFailed?.Invoke(this, new Win32Exception(Marshal.GetLastWin32Error()).Message);
        if (changed) ScreenChanged?.Invoke(this, EventArgs.Empty);
    }

    internal static PointF MapPoint(Rectangle bounds, double x, double y) => new(
        (float)(bounds.Left + x * Math.Max(0, bounds.Width - 1)),
        (float)(bounds.Top + y * Math.Max(0, bounds.Height - 1)));

    internal static Color ParseColor(string color)
    {
        if (color is null || color.Length != 7 || color[0] != '#'
            || !uint.TryParse(color.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var rgb))
            throw new ArgumentException("Color must be #RRGGBB.", nameof(color));
        return Color.FromArgb(255, (int)(rgb >> 16), (int)((rgb >> 8) & 255), (int)(rgb & 255));
    }

    internal static float TrailOpacity(double age, int duration) =>
        duration <= 0 ? 0 : (float)Math.Clamp(1 - age / duration, 0, 1);

    private void RenderFrame()
    {
        if (!_enabled || IsDisposed) return;
        var now = _clock.Elapsed.TotalMilliseconds;
        var elapsed = Math.Clamp(now - _lastFrame, 1, 50);
        _lastFrame = now;
        _trail.RemoveAll(point => now - point.Time >= _trailMs);
        var moving = false;
        if (_active)
        {
            var previous = _display;
            var dx = _target.X - _display.X;
            var dy = _target.Y - _display.Y;
            var distance = Math.Sqrt(dx * dx + dy * dy);
            var smoothingMs = _smoothing switch { "low" => 0d, "high" => 30d, _ => 16d };
            if (distance > 120) smoothingMs = Math.Min(smoothingMs, 8);
            var alpha = smoothingMs == 0 ? 1 : 1 - Math.Exp(-elapsed / smoothingMs);
            _display = distance < 0.25 ? _target : new((float)(_display.X + dx * alpha), (float)(_display.Y + dy * alpha));
            moving = Math.Abs(_target.X - _display.X) + Math.Abs(_target.Y - _display.Y) >= 0.25;
            if (_trailMs > 0 && Distance(previous, _display) > 0.1)
            {
                if (_trail.Count == 0) AddTrailPoint(previous, now - elapsed);
                AddTrailPoint(_display, now);
            }
        }
        if (!_active && _trail.Count == 0) { StopAndHide(); return; }
        try
        {
            PaintLayer(now);
        }
        catch (Exception exception) when (exception is ExternalException or OutOfMemoryException)
        {
            _enabled = false;
            _active = false;
            StopAndHide();
            RenderingFailed?.Invoke(this, exception.Message);
            return;
        }
        if (moving || _trail.Count > 0) _timer.Start();
        else _timer.Stop();
    }

    private void PaintLayer(double now)
    {
        var diameter = _size * Math.Max(96, OverlayNative.GetDpiForWindow(Handle)) / 96f;
        var padding = diameter * 1.5f + 3;
        var first = _active ? _display : _trail[0].Position;
        float minX = first.X, maxX = first.X, minY = first.Y, maxY = first.Y;
        foreach (var point in _trail)
        {
            minX = Math.Min(minX, point.Position.X); maxX = Math.Max(maxX, point.Position.X);
            minY = Math.Min(minY, point.Position.Y); maxY = Math.Max(maxY, point.Position.Y);
        }
        var bounds = Rectangle.Intersect(_screen.Bounds, Rectangle.FromLTRB(
            (int)Math.Floor(minX - padding), (int)Math.Floor(minY - padding),
            (int)Math.Ceiling(maxX + padding), (int)Math.Ceiling(maxY + padding)));
        if (bounds.IsEmpty) { StopAndHide(); return; }
        if (_surface is null || _surface.Width < bounds.Width || _surface.Height < bounds.Height
            || (long)_surface.Width * _surface.Height > (long)bounds.Width * bounds.Height * 4)
        {
            _surface?.Dispose();
            _surface = null;
            _surface = new LayeredSurface(Math.Min(_screen.Bounds.Width, (bounds.Width + 63) / 64 * 64),
                Math.Min(_screen.Bounds.Height, (bounds.Height + 63) / 64 * 64));
        }
        var graphics = _surface.Graphics;
        graphics.ResetTransform();
        graphics.Clear(Color.Transparent);
        graphics.TranslateTransform(-bounds.Left, -bounds.Top);
        for (var i = 1; i < _trail.Count; i++)
        {
            var opacity = TrailOpacity(now - _trail[i - 1].Time, _trailMs);
            using var glow = new Pen(Color.FromArgb((int)(opacity * 38), _color), diameter * 1.3f)
                { StartCap = LineCap.Round, EndCap = LineCap.Round };
            using var core = new Pen(Color.FromArgb((int)(opacity * 165), _color), diameter * 0.45f)
                { StartCap = LineCap.Round, EndCap = LineCap.Round };
            graphics.DrawLine(glow, _trail[i - 1].Position, _trail[i].Position);
            graphics.DrawLine(core, _trail[i - 1].Position, _trail[i].Position);
        }
        if (_active) DrawDot(graphics, _display, diameter, 1);
        else if (_trail.Count > 0) DrawDot(graphics, _trail[^1].Position, diameter, TrailOpacity(now - _trail[^1].Time, _trailMs));
        graphics.Flush(FlushIntention.Sync);
        var destination = new OverlayNative.Point(bounds.Left, bounds.Top);
        var size = new OverlayNative.Size(bounds.Width, bounds.Height);
        var source = new OverlayNative.Point(0, 0);
        var blend = new OverlayNative.Blend { ConstantAlpha = 255, AlphaFormat = 1 };
        if (!OverlayNative.UpdateLayeredWindow(Handle, 0, ref destination, ref size, _surface.Dc, ref source, 0, ref blend, 2))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        if (!Visible) Show();
    }

    private void DrawDot(Graphics graphics, PointF point, float diameter, float opacity)
    {
        for (var layer = 3; layer >= 1; layer--)
        {
            var size = diameter * layer;
            using var brush = new SolidBrush(Color.FromArgb((int)(opacity * (layer == 1 ? 255 : 20)), _color));
            graphics.FillEllipse(brush, point.X - size / 2, point.Y - size / 2, size, size);
        }
        using var center = new SolidBrush(Color.FromArgb((int)(opacity * 200), Color.White));
        var core = diameter * 0.22f;
        graphics.FillEllipse(center, point.X - core / 2, point.Y - core / 2, core, core);
    }

    private void AddTrailPoint(PointF position, double time)
    {
        if (_trail.Count >= MaximumTrailPoints) _trail.RemoveAt(0);
        _trail.Add(new(position, time));
    }

    private static double Distance(PointF first, PointF second) =>
        Math.Sqrt(Math.Pow(first.X - second.X, 2) + Math.Pow(first.Y - second.Y, 2));

    private void StopAndHide()
    {
        _timer.Stop();
        _trail.Clear();
        Hide();
        _surface?.Dispose();
        _surface = null;
    }

    private void DisplaySettingsChanged(object? sender, EventArgs args)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(() => { if (!IsDisposed) SelectScreen(SelectedScreenName); }); }
        catch (InvalidOperationException) { /* The UI can be shutting down after the event was queued. */ }
    }

    private void EnsureUiThread()
    {
        if (Environment.CurrentManagedThreadId != _uiThread)
            throw new InvalidOperationException("LaserOverlay must be called on its UI thread.");
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x0084) { message.Result = -1; return; }
        if (message.Msg == 0x0021) { message.Result = 3; return; }
        base.WndProc(ref message);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SystemEvents.DisplaySettingsChanged -= DisplaySettingsChanged;
            _timer.Dispose();
            _surface?.Dispose();
            _surface = null;
        }
        base.Dispose(disposing);
    }

    private readonly record struct TrailPoint(PointF Position, double Time);
}

internal sealed class LayeredSurface : IDisposable
{
    internal int Width { get; }
    internal int Height { get; }
    internal nint Dc { get; private set; }
    internal Graphics Graphics { get; private set; } = null!;
    private Bitmap? _bitmap;
    private nint _dib, _previous;

    internal LayeredSurface(int width, int height)
    {
        Width = width;
        Height = height;
        try
        {
            Dc = OverlayNative.CreateCompatibleDC(0);
            if (Dc == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            var info = new OverlayNative.BitmapInfo { Size = 40, Width = width, Height = -height, Planes = 1, BitCount = 32 };
            _dib = OverlayNative.CreateDIBSection(Dc, ref info, 0, out var pixels, 0, 0);
            if (_dib == 0 || pixels == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            _previous = OverlayNative.SelectObject(Dc, _dib);
            if (_previous == 0 || _previous == -1) throw new Win32Exception(Marshal.GetLastWin32Error());
            _bitmap = new Bitmap(width, height, checked(width * 4), PixelFormat.Format32bppPArgb, pixels);
            Graphics = System.Drawing.Graphics.FromImage(_bitmap);
            Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        }
        catch { Dispose(); throw; }
    }

    public void Dispose()
    {
        Graphics?.Dispose();
        _bitmap?.Dispose();
        if (Dc != 0 && _previous != 0 && _previous != -1) OverlayNative.SelectObject(Dc, _previous);
        if (_dib != 0) OverlayNative.DeleteObject(_dib);
        if (Dc != 0) OverlayNative.DeleteDC(Dc);
        _dib = _previous = Dc = 0;
    }
}

internal static class OverlayNative
{
    [StructLayout(LayoutKind.Sequential)] internal struct Point(int x, int y) { public int X = x, Y = y; }
    [StructLayout(LayoutKind.Sequential)] internal struct Size(int width, int height) { public int Width = width, Height = height; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] internal struct Blend { public byte Operation, Flags, ConstantAlpha, AlphaFormat; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct BitmapInfo
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, BitCount;
        public uint Compression, ImageSize;
        public int XPixelsPerMeter, YPixelsPerMeter;
        public uint ColorsUsed, ColorsImportant;
    }

    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UpdateLayeredWindow(nint window, nint destinationDc, ref Point destination,
        ref Size size, nint sourceDc, ref Point source, uint colorKey, ref Blend blend, uint flags);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(nint window);
    [DllImport("gdi32.dll", SetLastError = true)] internal static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll", SetLastError = true)] internal static extern nint CreateDIBSection(nint dc, ref BitmapInfo info, uint usage, out nint bits, nint section, uint offset);
    [DllImport("gdi32.dll", SetLastError = true)] internal static extern nint SelectObject(nint dc, nint obj);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool DeleteDC(nint dc);
}
