using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;
using SkiaSharp;

namespace Pointa.Windows.Native;

public sealed class LaserOverlay : Form
{
    private const int MaximumTrailPoints = 180;
    private readonly int _uiThread = Environment.CurrentManagedThreadId;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 16 };
    private readonly List<BezierTrail.Sample> _trail = new(MaximumTrailPoints);
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private Screen _screen;
    private LayeredSurface? _surface;
    private PointF _display;
    private bool _active, _enabled = true;
    private Color _color = Color.FromArgb(255, 100, 117);
    private int _size = 12, _trailMs = 500;
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
        if (_active && next == _display) return;
        if (!_active) _trail.Clear();
        _display = next;
        if (_trailMs > 0) AddTrailPoint(_display, _clock.Elapsed.TotalMilliseconds);
        _active = true;
        if (!_timer.Enabled) RenderFrame();
    }

    public void HidePointer(bool immediate = false)
    {
        EnsureUiThread();
        _active = false;
        if (immediate || _trailMs == 0 || _trail.Count < 2) StopAndHide();
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
        (float)(bounds.Left + x * bounds.Width),
        (float)(bounds.Top + y * bounds.Height));

    internal static Color ParseColor(string color)
    {
        if (color is null || color.Length != 7 || color[0] != '#'
            || !uint.TryParse(color.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var rgb))
            throw new ArgumentException("Color must be #RRGGBB.", nameof(color));
        return Color.FromArgb(255, (int)(rgb >> 16), (int)((rgb >> 8) & 255), (int)(rgb & 255));
    }

    private void RenderFrame()
    {
        if (!_enabled || IsDisposed) return;
        var now = _clock.Elapsed.TotalMilliseconds;
        BezierTrail.Drain(_trail, now, _trailMs);
        if (!_active && _trail.Count < 2) { StopAndHide(); return; }
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
        if (_trail.Count > 0) _timer.Start();
        else _timer.Stop();
    }

    private void PaintLayer(double now)
    {
        var scale = Math.Max(96, OverlayNative.GetDpiForWindow(Handle)) / 96f;
        var padding = _size * scale / 2 + 3 * _size / 2f + 3;
        var head = new BezierTrail.Point(Math.Clamp(_display.X, _screen.Bounds.Left + 8*scale, _screen.Bounds.Right - 8*scale),
            Math.Clamp(_display.Y, _screen.Bounds.Top + 8*scale, _screen.Bounds.Bottom - 8*scale));
        var tension = _smoothing switch { "low" => .15, "high" => .45, _ => .3 };
        var segments = BezierTrail.Build(_trail, 8 * scale * _size / 12, tension, scale);
        var first = _active ? head : _trail[0].Position;
        double minX = first.X, maxX = first.X, minY = first.Y, maxY = first.Y;
        void Include(BezierTrail.Point point)
        {
            minX = Math.Min(minX, point.X); maxX = Math.Max(maxX, point.X);
            minY = Math.Min(minY, point.Y); maxY = Math.Max(maxY, point.Y);
        }
        foreach (var point in _trail) Include(point.Position);
        foreach (var segment in segments) { Include(segment.Start); Include(segment.Control1); Include(segment.Control2); Include(segment.End); }
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
        var canvas = _surface.Surface.Canvas;
        canvas.ResetMatrix();
        canvas.Clear(SKColors.Transparent);
        canvas.Translate(-bounds.Left, -bounds.Top);
        var color = new SKColor(_color.R, _color.G, _color.B);
        BezierTrail.Draw(canvas, _trail, segments, BezierTrail.Fade(_trail, now, _trailMs), color, scale, _size);
        if (_active) BezierTrail.DrawHead(canvas, head, color, scale, _size);
        _surface.Surface.Flush();
        var destination = new OverlayNative.Point(bounds.Left, bounds.Top);
        var size = new OverlayNative.Size(bounds.Width, bounds.Height);
        var source = new OverlayNative.Point(0, 0);
        var blend = new OverlayNative.Blend { ConstantAlpha = 255, AlphaFormat = 1 };
        if (!OverlayNative.UpdateLayeredWindow(Handle, 0, ref destination, ref size, _surface.Dc, ref source, 0, ref blend, 2))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        if (!Visible) Show();
    }

    private void AddTrailPoint(PointF position, double time)
    {
        if (_trail.Count >= MaximumTrailPoints) _trail.RemoveAt(0);
        _trail.Add(new(new(position.X, position.Y), time));
    }

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

}

internal sealed class LayeredSurface : IDisposable
{
    internal int Width { get; }
    internal int Height { get; }
    internal nint Dc { get; private set; }
    internal SKSurface Surface { get; private set; } = null!;
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
            Surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul), pixels, checked(width * 4))
                ?? throw new OutOfMemoryException("Could not allocate the Skia overlay surface.");
        }
        catch { Dispose(); throw; }
    }

    public void Dispose()
    {
        Surface?.Dispose();
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
