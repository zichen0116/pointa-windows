using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Pointa.Windows.Native;

public sealed class PresentationController
{
    private readonly int _uiThread = Environment.CurrentManagedThreadId;
    private long _lastPage = -1000;
    public bool AllowPresentationApps { get; set; } = true;

    public (bool Success, string Code) TryPage(bool next, CancellationToken cancellation = default)
    {
        EnsureUiThread();
        var foreground = ReadForeground();
        if (foreground is null) return (false, "no_foreground_window");
        if (!IsAllowedTarget(foreground.ProcessName, foreground.ClassName, AllowPresentationApps))
            return (false, "presentation_not_focused");
        if (Environment.TickCount64 - _lastPage < 180) return (false, "page_cooldown");
        foreach (var key in new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C, 0x25, 0x27 })
            if ((InputNative.GetAsyncKeyState(key) & 0x8000) != 0) return (false, "keyboard_busy");

        // SendInput targets the foreground globally; check again immediately before the atomic pair.
        if (InputNative.GetForegroundWindow() != foreground.Handle) return (false, "focus_changed");
        var keyCode = (ushort)(next ? 0x27 : 0x25);
        var input = new[] { InputNative.Keyboard(keyCode, false), InputNative.Keyboard(keyCode, true) };
        if (cancellation.IsCancellationRequested) return (false, "disconnected");
        _lastPage = Environment.TickCount64;
        var sent = InputNative.SendInput((uint)input.Length, input, Marshal.SizeOf<InputNative.Input>());
        if (sent == 2) return (true, "ok");
        if (sent == 1)
            InputNative.SendInput(1, [InputNative.Keyboard(keyCode, true)], Marshal.SizeOf<InputNative.Input>());
        return (false, "input_blocked");
    }

    internal static bool IsAllowedTarget(string processName, string className, bool allowPresentationApps) =>
        IsAutomaticTarget(processName, className) || allowPresentationApps && IsPresentationProcess(processName);

    internal static bool IsAutomaticTarget(string processName, string className) =>
        (processName.Equals("POWERPNT", StringComparison.OrdinalIgnoreCase)
            || processName.Equals("wpp", StringComparison.OrdinalIgnoreCase))
        && className.Equals("screenClass", StringComparison.OrdinalIgnoreCase);

    internal static bool IsPresentationProcess(string processName) => processName.ToLowerInvariant() is
        "msedge" or "chrome" or "firefox" or "brave" or "vivaldi" or "opera" or "arc"
        or "acrord32" or "acrobat" or "sumatrapdf" or "foxitpdfreader" or "foxitreader" or "pdfxedit"
        or "wpp" or "powerpnt";

    private static WindowIdentity? ReadForeground()
    {
        var handle = InputNative.GetForegroundWindow();
        if (handle == 0 || !InputNative.IsWindowVisible(handle)) return null;
        InputNative.GetWindowThreadProcessId(handle, out var processId);
        if (processId == 0 || processId == Environment.ProcessId) return null;
        try
        {
            using var process = Process.GetProcessById(checked((int)processId));
            var className = new StringBuilder(256);
            if (InputNative.GetClassName(handle, className, className.Capacity) == 0) return null;
            return new(handle, process.ProcessName, className.ToString());
        }
        catch (Exception exception) when (exception is Win32Exception or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    private void EnsureUiThread()
    {
        if (Environment.CurrentManagedThreadId != _uiThread)
            throw new InvalidOperationException("PresentationController must be called on its UI thread.");
    }

    private sealed record WindowIdentity(nint Handle, string ProcessName, string ClassName);
}

internal static class InputNative
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct Input { public uint Type; public InputUnion Data; }

    [StructLayout(LayoutKind.Explicit)]
    internal struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
    }

    // The unused mouse member supplies INPUT's required union size and pointer alignment.
    [StructLayout(LayoutKind.Sequential)]
    internal struct MouseInput
    {
        public int X, Y;
        public uint MouseData, Flags, Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KeyboardInput
    {
        public ushort Key, Scan;
        public uint Flags, Time;
        public nuint ExtraInfo;
    }

    internal static Input Keyboard(ushort key, bool keyUp) => new()
    {
        Type = 1,
        Data = new InputUnion { Keyboard = new KeyboardInput { Key = key, Flags = (uint)(1 | (keyUp ? 2 : 0)) } }
    };

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetClassName(nint window, StringBuilder className, int maximum);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowText(nint window, StringBuilder text, int maximum);
}
