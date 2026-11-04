using System.Net;
using System.Net.NetworkInformation;
using Microsoft.Win32;
using Pointa.Core;
using Pointa.Server;
using Pointa.Windows.Native;

namespace Pointa.Windows;

internal sealed class PointaContext : ApplicationContext
{
    private AppSettings settings;
    private PairingForm form;
    private readonly string directory;
    private readonly NotifyIcon tray;
    private readonly LaserOverlay overlay = new();
    private readonly PresentationController presentation = new();
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 1000 };
    private readonly SemaphoreSlim serverGate = new(1);
    private HostService? host;
    private IReadOnlyList<IPAddress> addresses = [];
    private string? lastDiagnostic;
    private bool exiting, overlayEnabled = true, restarting;
    private DateTimeOffset nextNetworkCheck;
    private DateTimeOffset? armAt;
    private readonly string? smokeDirectory;

    public PointaContext(string directory, AppSettings settings, bool background, string? smokeDirectory)
    {
        this.directory = directory;
        this.settings = settings;
        this.smokeDirectory = smokeDirectory;
        form = CreateForm();
        _ = form.Handle;
        overlay.SelectScreen(settings.ScreenName);
        overlay.UpdateEffect(settings.LaserColor, settings.LaserSize, settings.TrailMs, settings.Smoothing);
        overlay.RenderingFailed += (_, error) => { lastDiagnostic = error; form.SetOverlayStatus(T("覆盖层绘制失败", "Overlay rendering failed")); };
        overlay.ScreenChanged += (_, _) =>
        {
            if (settings.ScreenName != overlay.SelectedScreenName)
            {
                settings.ScreenName = overlay.SelectedScreenName;
                TrySaveSettings();
                Notify(T("显示器已更改", "Display changed"), T("激光已切换到可用显示器。", "The laser moved to an available display."));
            }
        };
        tray = new NotifyIcon { Icon = Ui.CreateIcon(), Text = "映点 Pointa", Visible = true };
        tray.DoubleClick += (_, _) => ShowPairing();
        tray.BalloonTipClicked += (_, _) => ShowPairing();
        BuildTray();
        timer.Tick += Tick;
        timer.Start();
        NetworkChange.NetworkAddressChanged += NetworkChanged;
        SystemEvents.PowerModeChanged += PowerChanged;
        if (!background) form.Show();
        form.BeginInvoke(async () =>
        {
            await RestartServerAsync();
            if (smokeDirectory is not null) await RunSmokeAsync();
        });
    }

    private PairingForm CreateForm()
    {
        var window = new PairingForm(settings);
        window.RefreshRequested += (_, _) => { host?.RefreshPairing(); RefreshUi(); };
        window.DisconnectRequested += (_, _) => { host?.Disconnect(); overlay.HidePointer(true); };
        window.SettingsRequested += (_, _) => OpenSettings();
        window.DevicesRequested += (_, _) => OpenDevices();
        return window;
    }

    public void ShowPairing()
    {
        if (exiting) return;
        form.Show();
        form.WindowState = FormWindowState.Normal;
        form.Activate();
    }

    public async void RequestQuit() => await QuitAsync();

    public void Dispatch(Action action)
    {
        if (exiting || form.IsDisposed) return;
        try { if (form.InvokeRequired) form.BeginInvoke(action); else action(); }
        catch (InvalidOperationException) when (exiting || form.IsDisposed) { }
    }

    private async Task RestartServerAsync()
    {
        await serverGate.WaitAsync();
        try
        {
            if (exiting) return;
            restarting = true;
            overlay.HidePointer(true);
            var previous = host;
            host = null;
            if (previous is not null) await previous.DisposeAsync();
            addresses = smokeDirectory is not null ? [IPAddress.Loopback] : NetworkPolicy.GetAddresses(settings.AllowPublicNetworks);
            var current = new HostService(new HostOptions(directory, addresses, Environment.MachineName), ApproveAsync);
            PointerState? latestPointer = null;
            var pointerGate = new object();
            var pointerQueued = 0;
            long lastPointerRevision = -1;
            current.PointerChanged += state =>
            {
                lock (pointerGate)
                {
                    if (host != current || exiting || state.Revision <= lastPointerRevision) return;
                    lastPointerRevision = state.Revision;
                    Interlocked.Exchange(ref latestPointer, state);
                }
                if (Interlocked.Exchange(ref pointerQueued, 1) == 0) Dispatch(() =>
                {
                    Interlocked.Exchange(ref pointerQueued, 0);
                    var pointer = Interlocked.Exchange(ref latestPointer, null);
                    if (pointer is null || host != current) return;
                    if (pointer.Active && current.ConnectedDevice is not null) overlay.MovePointer(pointer.X, pointer.Y);
                    else overlay.HidePointer();
                });
            };
            current.EffectChanged += effect => Dispatch(() =>
            {
                if (host != current) return;
                overlay.UpdateEffect(effect.Color, effect.Size, effect.TrailMs, effect.Smoothing);
                settings.LaserColor = effect.Color;
                settings.LaserSize = effect.Size;
                settings.TrailMs = effect.TrailMs;
                settings.Smoothing = effect.Smoothing;
                TrySaveSettings();
            });
            current.SlideRequested = (direction, sessionCancellation) =>
            {
                if (host != current || exiting) return new SlideResult(false, "disconnected");
                SlideResult Page()
                {
                    if (host != current || sessionCancellation.IsCancellationRequested || current.ConnectedDevice is null) return new SlideResult(false, "disconnected");
                    var result = presentation.TryPage(direction == SlideDirection.Next, sessionCancellation);
                    return new SlideResult(result.Success, result.Success ? null : result.Code);
                }
                try { return form.InvokeRequired ? (SlideResult)form.Invoke(Page) : Page(); }
                catch (InvalidOperationException) { return new SlideResult(false, "shutting_down"); }
            };
            current.StateChanged += () => Dispatch(() =>
            {
                if (host != current) return;
                if (current.ConnectedDevice is null) { latestPointer = null; overlay.HidePointer(true); }
                RefreshUi();
            });
            current.Diagnostic += text => Dispatch(() => { lastDiagnostic = text; });
            host = current;
            current.SetEffect(new LaserEffect(settings.LaserColor, settings.LaserSize, settings.TrailMs, settings.Smoothing));
            await current.StartAsync();
            RefreshUi();
        }
        catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException or InvalidOperationException or System.Security.Cryptography.CryptographicException or UnauthorizedAccessException)
        {
            lastDiagnostic = ex.Message;
            var failed = host;
            host = null;
            if (failed is not null) await failed.DisposeAsync();
            form.SetError(ex.Message);
        }
        finally { restarting = false; serverGate.Release(); }
    }

    private async Task<bool> ApproveAsync(PairingRequest request, CancellationToken cancellation)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellation.Register(() => completion.TrySetResult(false));
        Dispatch(async () =>
        {
            if (completion.Task.IsCompleted) return;
            using var approval = new ApprovalForm(settings, request);
            using var cancelUi = cancellation.Register(() => Dispatch(() => { if (!approval.IsDisposed) approval.Close(); }));
            approval.Show();
            Notify(T("手机请求配对", "Phone pairing request"), request.DeviceName);
            completion.TrySetResult(await approval.Decision);
        });
        return await completion.Task;
    }

    private void Tick(object? sender, EventArgs args)
    {
        if (smokeDirectory is null && !restarting && DateTimeOffset.UtcNow >= nextNetworkCheck)
        {
            nextNetworkCheck = DateTimeOffset.UtcNow.AddSeconds(10);
            var allowed = NetworkPolicy.GetAddresses(settings.AllowPublicNetworks);
            if (!new HashSet<IPAddress>(allowed).SetEquals(addresses)) { _ = RestartServerAsync(); return; }
        }
        if (armAt is not null && DateTimeOffset.UtcNow >= armAt)
        {
            armAt = null;
            var result = presentation.ArmForegroundWindow();
            Notify(T("演示窗口", "Presentation window"), result.Success ? T("已允许当前窗口翻页。", "Paging enabled for the current window.") : T("无法允许此窗口，请切到浏览器或 PDF。", "Switch to a browser or PDF window and try again."));
        }
        if (host is null || restarting) return;
        if (host.Pairing.ExpiresAt <= DateTimeOffset.UtcNow) host.RefreshPairing();
        RefreshUi();
    }

    private void RefreshUi()
    {
        if (host is null || exiting) return;
        var pairing = host.Pairing;
        form.UpdatePairing(pairing.QrPayload, pairing.Code, pairing.ExpiresAt, addresses.Any(x => !IPAddress.IsLoopback(x)), host.ConnectedDevice?.Name);
        tray.Text = host.ConnectedDevice is null ? "映点 Pointa" : T("映点 · 手机已连接", "Pointa · Phone connected");
    }

    private void BuildTray()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(T("打开配对", "Open pairing"), null, (_, _) => ShowPairing());
        var visible = new ToolStripMenuItem(T("允许显示激光", "Enable laser")) { Checked = overlayEnabled, CheckOnClick = true };
        visible.CheckedChanged += (_, _) => { overlayEnabled = visible.Checked; overlay.SetEnabled(overlayEnabled); };
        menu.Items.Add(visible);
        menu.Items.Add(T("紧急隐藏并禁用激光", "Hide and disable laser"), null, (_, _) => { visible.Checked = false; overlay.HidePointer(true); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(T("允许当前演示窗口（3 秒后）", "Allow foreground slides (in 3 seconds)"), null, (_, _) =>
        {
            form.Hide();
            armAt = DateTimeOffset.UtcNow.AddSeconds(3);
            Notify(T("选择演示窗口", "Choose presentation window"), T("请在 3 秒内切换到浏览器或 PDF 演示。", "Switch to your browser or PDF presentation within 3 seconds."));
        });
        menu.Items.Add(T("取消窗口授权", "Clear presentation window"), null, (_, _) => presentation.ClearTarget());
        var displays = new ToolStripMenuItem(T("激光显示器", "Laser display"));
        displays.DropDownOpening += (_, _) =>
        {
            displays.DropDownItems.Clear();
            foreach (var screen in Screen.AllScreens)
            {
                var item = new ToolStripMenuItem($"{screen.DeviceName} · {screen.Bounds.Width} × {screen.Bounds.Height}") { Checked = screen.DeviceName == overlay.SelectedScreenName };
                item.Click += (_, _) => { overlay.SelectScreen(screen.DeviceName); settings.ScreenName = screen.DeviceName; TrySaveSettings(); };
                displays.DropDownItems.Add(item);
            }
        };
        menu.Items.Add(displays);
        menu.Items.Add(T("设备管理", "Devices"), null, (_, _) => OpenDevices());
        menu.Items.Add(T("设置与连接帮助", "Settings and connection help"), null, (_, _) => OpenSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(T("退出映点", "Quit Pointa"), null, async (_, _) => await QuitAsync());
        tray.ContextMenuStrip?.Dispose();
        tray.ContextMenuStrip = menu;
    }

    private void OpenSettings()
    {
        var pairing = host?.Pairing;
        var diagnostic = $"Pointa 0.1.0 · {Environment.OSVersion}\r\n" +
            $"TLS port: {pairing?.Port ?? 32145} / UDP: 32146\r\n" +
            $"LAN: {string.Join(", ", addresses.Where(x => !IPAddress.IsLoopback(x)))}\r\n" +
            $"Display: {overlay.SelectedScreenName}\r\n" +
            $"Target: {presentation.TargetDescription}\r\n" +
            $"Status: {lastDiagnostic ?? "OK"}\r\n" +
            T("只共享应用窗口时可能看不到激光；请共享整个屏幕。", "Application-only sharing may omit the laser; share the whole screen.");
        bool networkChanged = false, languageChanged = false;
        var redacted = System.Text.RegularExpressions.Regex.Replace(diagnostic, @"\b(\d{1,3}\.\d{1,3}\.)\d{1,3}\.\d{1,3}\b", "$1*.*")
            .Replace(presentation.TargetDescription, T("已隐藏窗口标题", "Window title redacted"))
            .Replace(directory, "[Pointa data]");
        using var dialog = new SettingsForm(settings, diagnostic, next =>
        {
            next.ApplyStartup();
            try { next.Save(directory); }
            catch { settings.ApplyStartup(); throw; }
            networkChanged = next.AllowPublicNetworks != settings.AllowPublicNetworks;
            languageChanged = next.Language != settings.Language;
            settings = next;
            overlay.SelectScreen(settings.ScreenName);
            overlay.UpdateEffect(settings.LaserColor, settings.LaserSize, settings.TrailMs, settings.Smoothing);
            host?.SetEffect(new LaserEffect(settings.LaserColor, settings.LaserSize, settings.TrailMs, settings.Smoothing));
        }, redacted);
        if (dialog.ShowDialog(form.Visible ? form : null) != DialogResult.OK) return;
        if (languageChanged)
        {
            var wasVisible = form.Visible;
            form.Exiting = true;
            form.Close();
            form.Dispose();
            form = CreateForm();
            _ = form.Handle;
            if (wasVisible) form.Show();
            BuildTray();
        }
        if (networkChanged || host is null) _ = RestartServerAsync();
        else RefreshUi();
    }

    private void OpenDevices()
    {
        if (host is null) return;
        using var dialog = new DevicesForm(settings, host);
        dialog.ShowDialog(form.Visible ? form : null);
    }

    private void TrySaveSettings()
    {
        try { settings.Save(directory); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { lastDiagnostic = ex.Message; }
    }

    private void Notify(string title, string text)
    {
        if (smokeDirectory is null) tray.ShowBalloonTip(3500, title, text, ToolTipIcon.Info);
    }

    private void NetworkChanged(object? sender, EventArgs args) => Dispatch(async () =>
    {
        if (!restarting) await RestartServerAsync();
    });
    private void PowerChanged(object sender, PowerModeChangedEventArgs args) => Dispatch(async () =>
    {
        if (args.Mode == PowerModes.Suspend) { host?.Disconnect(); overlay.HidePointer(true); }
        else if (args.Mode == PowerModes.Resume && !restarting) await RestartServerAsync();
    });

    private async Task RunSmokeAsync()
    {
        Directory.CreateDirectory(smokeDirectory!);
        try
        {
            if (host is null) throw new InvalidOperationException("Server startup failed: " + lastDiagnostic);
            NativeSelfCheck.Run();
            NativeSelfCheck.RunOverlayChecks();
            var settingsDirectory = Path.Combine(smokeDirectory!, "settings-check");
            var sample = new AppSettings { LaserColor = "#32B894", TrailMs = 800, Language = "en" };
            sample.Save(settingsDirectory);
            if (AppSettings.Load(settingsDirectory).LaserColor != sample.LaserColor) throw new InvalidOperationException("Settings round trip failed.");
            File.WriteAllText(Path.Combine(settingsDirectory, "settings.json"), "{broken");
            bool corruptRejected = false;
            try { AppSettings.Load(settingsDirectory); } catch (System.Text.Json.JsonException) { corruptRejected = true; }
            if (!corruptRejected || File.ReadAllText(Path.Combine(settingsDirectory, "settings.json")) != "{broken")
                throw new InvalidOperationException("Corrupt settings were overwritten or accepted.");
            using (var icon = Ui.CreateIcon())
            using (var iconFile = File.Create(Path.Combine(smokeDirectory!, "Pointa.ico"))) icon.Save(iconFile);
            var p = host.Pairing;
            form.UpdatePairing(p.QrPayload, p.Code, p.ExpiresAt, true, null);
            form.SavePreview(Path.Combine(smokeDirectory!, "pairing-window.png"));
            using (var english = new PairingForm(new AppSettings { Language = "en" }))
            {
                _ = english.Handle;
                english.Show();
                english.UpdatePairing(p.QrPayload, p.Code, p.ExpiresAt, true, null);
                english.SavePreview(Path.Combine(smokeDirectory!, "pairing-window-en.png"));
            }
            using (var options = new SettingsForm(settings, "Pointa smoke test · loopback only", _ => { }))
            {
                _ = options.Handle;
                options.Show();
                using var bitmap = new Bitmap(options.Width, options.Height);
                options.DrawToBitmap(bitmap, new Rectangle(0, 0, options.Width, options.Height));
                bitmap.Save(Path.Combine(smokeDirectory!, "settings-window.png"));
            }
            var beforeMouse = Cursor.Position;
            overlay.MovePointer(.25, .75);
            await Task.Delay(100);
            overlay.HidePointer(true);
            if (Cursor.Position != beforeMouse) throw new InvalidOperationException("Pointer moved the system cursor.");
            File.WriteAllText(Path.Combine(smokeDirectory!, "smoke-result.txt"), "PASS: host started; native structs, mapping, foreground policies, overlay styles/focus/mouse/idle/expiry checks passed; settings persistence and corrupt-file preservation passed; Chinese/English pairing + settings rendered.");
        }
        catch (Exception ex)
        {
            Environment.ExitCode = 1;
            File.WriteAllText(Path.Combine(smokeDirectory!, "smoke-result.txt"), "FAIL: " + ex);
        }
        await QuitAsync();
    }

    private async Task QuitAsync()
    {
        if (exiting) return;
        exiting = true;
        timer.Stop();
        NetworkChange.NetworkAddressChanged -= NetworkChanged;
        SystemEvents.PowerModeChanged -= PowerChanged;
        overlay.HidePointer(true);
        await serverGate.WaitAsync();
        try { if (host is not null) await host.DisposeAsync(); host = null; }
        finally { serverGate.Release(); }
        tray.Visible = false;
        tray.ContextMenuStrip?.Dispose();
        tray.Icon?.Dispose();
        tray.Dispose();
        overlay.Dispose();
        form.Exiting = true;
        form.Close();
        form.Dispose();
        timer.Dispose();
        ExitThread();
    }

    private string T(string zh, string en) => settings.T(zh, en);
}
