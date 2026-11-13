using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Pointa.Windows.Native;

public static class NativeSelfCheck
{
    public static void Run()
    {
        Check(Marshal.SizeOf<InputNative.Input>() == (IntPtr.Size == 8 ? 40 : 28), "INPUT native size");
        Check(Marshal.OffsetOf<InputNative.Input>(nameof(InputNative.Input.Data)).ToInt32()
            == (IntPtr.Size == 8 ? 8 : 4), "INPUT union alignment");
        Check(Marshal.SizeOf<OverlayNative.BitmapInfo>() == 40, "BITMAPINFOHEADER native size");
        Check(Marshal.SizeOf<OverlayNative.Blend>() == 4, "BLENDFUNCTION native size");
        var leftMonitor = new Rectangle(-3840, -400, 3840, 2160);
        Check(LaserOverlay.MapPoint(leftMonitor, 0, 0) == new PointF(-3840, -400), "negative monitor origin");
        Check(LaserOverlay.MapPoint(leftMonitor, 1, 1) == new PointF(0, 1760), "canvas edge mapping");
        Check(LaserOverlay.MapPoint(new Rectangle(0, 0, 1080, 1920), 0.5, 0.5)
            == new PointF(540, 960), "portrait mapping");
        BezierTrail.Sample[] samples = [new(new(0, 0), 0)];
        Check(BezierTrail.Fade(samples, 0, 500) == 1 && BezierTrail.Fade(samples, 250, 500) == .5
            && BezierTrail.Fade(samples, 501, 500) == 0 && BezierTrail.Fade(samples, 0, 0) == 0, "trail lifecycle");
        Check(LaserOverlay.ParseColor("#FF6475").ToArgb() == Color.FromArgb(255, 100, 117).ToArgb(), "effect color");
        foreach (var invalid in new[] { "red", "#GG0000", "#00000000", "#00000 " })
        {
            var rejected = false;
            try { LaserOverlay.ParseColor(invalid); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "invalid color rejected");
        }
        Check(PresentationController.IsAutomaticTarget("POWERPNT", "screenClass"), "PowerPoint slideshow accepted");
        Check(!PresentationController.IsAutomaticTarget("POWERPNT", "PPTFrameClass"), "PowerPoint editor denied");
        Check(!PresentationController.IsAutomaticTarget("wpp", "OpusApp"), "WPS editor denied");
        Check(!PresentationController.IsAutomaticTarget("chrome", "Chrome_WidgetWin_1"), "browser needs consent");
        Check(PresentationController.CanArmProcess("msedge") && PresentationController.CanArmProcess("Acrobat")
            && !PresentationController.CanArmProcess("notepad"), "explicit target process boundary");
        var down = InputNative.Keyboard(0x27, false);
        var up = InputNative.Keyboard(0x27, true);
        Check(down.Type == 1 && down.Data.Keyboard.Key == 0x27 && down.Data.Keyboard.Flags == 1
            && up.Data.Keyboard.Flags == 3, "extended arrow down/up pair");
    }

    // Explicit interactive check: briefly draws the overlay, never sends keys or moves the real mouse.
    public static void RunOverlayChecks()
    {
        Check(Thread.CurrentThread.GetApartmentState() == ApartmentState.STA, "STA thread required");
        var cursor = Cursor.Position;
        var foreground = InputNative.GetForegroundWindow();
        using var overlay = new LaserOverlay();
        string? failure = null;
        overlay.RenderingFailed += (_, message) => failure = message;
        var styles = GetWindowLong(overlay.Handle, -20);
        const int requiredStyles = 0x00080000 | 0x00000020 | 0x08000000 | 0x00000080;
        Check((styles & requiredStyles) == requiredStyles, "layered / click-through / no-activate / toolwindow");
        overlay.UpdateEffect("#FF6475", 16, 0, "low");
        overlay.MovePointer(0.25, 0.25);
        Check(failure is null && overlay.Visible, $"layered rendering: {failure}");
        Check(!overlay.IsAnimationRunning, "stationary trail-free head has no timer");
        overlay.MovePointer(0.75, 0.75);
        Check(Cursor.Position == cursor, "system cursor preserved");
        Check(InputNative.GetForegroundWindow() == foreground, "foreground preserved");
        overlay.HidePointer(true);
        Check(!overlay.Visible && !overlay.IsAnimationRunning, "emergency hide stops rendering");
        overlay.UpdateEffect("#FF6475", 16, 250, "standard");
        overlay.MovePointer(0.5, 0.5);
        overlay.HidePointer();
        var timeout = Stopwatch.StartNew();
        while (overlay.IsAnimationRunning && timeout.ElapsedMilliseconds < 2000)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
        Check(!overlay.Visible && !overlay.IsAnimationRunning, "trail expires into idle");
        overlay.SetEnabled(false);
        overlay.MovePointer(0.5, 0.5);
        Check(!overlay.Visible, "disabled laser rejects movement");
        overlay.SelectScreen("missing-screen-for-self-check");
        Check(overlay.SelectedScreenName == (Screen.PrimaryScreen ?? Screen.AllScreens[0]).DeviceName, "missing monitor fallback");
        Check(failure is null, $"native rendering error: {failure}");
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException($"Native self-check failed: {name}");
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(nint window, int index);
}
