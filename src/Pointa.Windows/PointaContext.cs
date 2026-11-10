using System.Net;
using System.Net.NetworkInformation;
using Microsoft.Win32;
using Pointa.Core;
using Pointa.Server;
using Pointa.Windows.Native;
using W = System.Windows;

namespace Pointa.Windows;

internal sealed class PointaContext : ApplicationContext
{
    private AppSettings settings;
    private PairingWindow form;
    private TrayMenu? trayMenu;
    private SettingsWindow? settingsWindow;
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
            UpdateDisplay();
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
        tray.MouseUp += (_, e) => { if (e.Button == MouseButtons.Right && trayMenu is not null) trayMenu.IsOpen = true; };
        BuildTray();
        timer.Tick += Tick;
        timer.Start();
        NetworkChange.NetworkAddressChanged += NetworkChanged;
        SystemEvents.PowerModeChanged += PowerChanged;
        if (!background) form.Show();
        form.Dispatcher.InvokeAsync(async () =>
        {
            await RestartServerAsync();
            if (smokeDirectory is not null) await RunSmokeAsync();
        });
    }

    private PairingWindow CreateForm()
    {
        var window = new PairingWindow(settings);
        W.Application.Current.MainWindow = window;
        window.RefreshRequested += (_, _) => { host?.RefreshPairing(); RefreshUi(); };
        window.DisconnectRequested += (_, _) => { host?.Disconnect(); overlay.HidePointer(true); };
        window.SettingsRequested += (_, _) => OpenSettings();
        return window;
    }

    public void ShowPairing()
    {
        if (exiting) return;
        form.Show();
        form.WindowState = W.WindowState.Normal;
        form.Activate();
    }

    public async void RequestQuit() => await QuitAsync();

    public void Dispatch(Action action)
    {
        if (exiting || form.IsClosed) return;
        try { if (form.Dispatcher.CheckAccess()) action(); else form.Dispatcher.BeginInvoke(action); }
        catch (InvalidOperationException) when (exiting || form.IsClosed) { }
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
            addresses = smokeDirectory is not null ? [IPAddress.Loopback] : NetworkPolicy.GetAddresses();
            var port = 32145;
            if (smokeDirectory is not null)
            {
                var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
                probe.Start(); port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
            }
            var current = new HostService(new HostOptions(directory, addresses, Environment.MachineName, port, smokeDirectory is null ? 32146 : 0), ApproveAsync);
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
                try { return form.Dispatcher.CheckAccess() ? Page() : form.Dispatcher.Invoke(Page); }
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
            UpdateDisplay();
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
            using var approval = new ApprovalWindow(settings, request);
            using var cancelUi = cancellation.Register(() => Dispatch(() => { if (!approval.IsClosed) approval.Close(); }));
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
            var allowed = NetworkPolicy.GetAddresses();
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
        form.UpdatePairing(pairing.QrPayload, pairing.Code, pairing.ExpiresAt, addresses.Any(x => !IPAddress.IsLoopback(x)), host.ConnectedDevice?.Name, pairing.Host);
        tray.Text = host.ConnectedDevice is null ? "映点 Pointa" : T("映点 · 手机已连接", "Pointa · Phone connected");
    }

    private void UpdateDisplay()
    {
        var screen = Screen.AllScreens.FirstOrDefault(x => x.DeviceName == overlay.SelectedScreenName);
        host?.SetDisplay(screen is null ? null : new DisplayInfo(screen.Bounds.Width, screen.Bounds.Height));
    }

    private void BuildTray()
    {
        var menu = new TrayMenu();
        menu.Add(T("打开配对", "Open pairing"), ShowPairing);
        var visible = menu.Add(T("允许显示激光", "Enable laser"), () => { });
        visible.IsCheckable = true; visible.IsChecked = overlayEnabled;
        visible.Checked += (_, _) => { overlayEnabled = true; overlay.SetEnabled(true); };
        visible.Unchecked += (_, _) => { overlayEnabled = false; overlay.SetEnabled(false); };
        menu.Add(T("紧急隐藏并禁用激光", "Hide and disable laser"), () => { visible.IsChecked = false; overlay.HidePointer(true); });
        menu.Items.Add(new System.Windows.Controls.Separator());
        menu.Add(T("允许当前演示窗口（3 秒后）", "Allow foreground slides (in 3 seconds)"), () =>
        {
            form.Hide();
            armAt = DateTimeOffset.UtcNow.AddSeconds(3);
            Notify(T("选择演示窗口", "Choose presentation window"), T("请在 3 秒内切换到浏览器或 PDF 演示。", "Switch to your browser or PDF presentation within 3 seconds."));
        });
        menu.Add(T("取消窗口授权", "Clear presentation window"), () => presentation.ClearTarget());
        menu.Add(T("设置", "Settings"), OpenSettings);
        menu.Items.Add(new System.Windows.Controls.Separator());
        menu.Add(T("退出映点", "Quit Pointa"), async () => await QuitAsync());
        if (trayMenu is not null) trayMenu.IsOpen = false;
        trayMenu = menu;
    }

    private void OpenSettings()
    {
        if (settingsWindow is { IsClosed: false }) { settingsWindow.Activate(); return; }
        bool languageChanged = false;
        using var dialog = new SettingsWindow(settings, () => host, (language, startup) =>
        {
            var next = settings.WithGeneralPreferences(language, startup);
            next.ApplyStartup();
            try { next.Save(directory); }
            catch { settings.ApplyStartup(); throw; }
            languageChanged = next.Language != settings.Language;
            settings = next;
        });
        settingsWindow = dialog;
        dialog.Closed += (_, _) => settingsWindow = null;
        if (form.IsVisible) dialog.Owner = form;
        dialog.ShowDialog();
        if (!dialog.Saved) return;
        if (languageChanged)
        {
            var wasVisible = form.IsVisible;
            form.Exiting = true;
            form.Close();
            form.Dispose();
            form = CreateForm();
            _ = form.Handle;
            if (wasVisible) form.Show();
            BuildTray();
        }
        if (host is null) _ = RestartServerAsync();
        else RefreshUi();
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
            var available = NetworkPolicy.GetAddresses();
            if (!available.Contains(IPAddress.Loopback) || available.Any(ip => !IPAddress.IsLoopback(ip) && !NetworkPolicy.IsLanV4(ip)))
                throw new InvalidOperationException("Network selection escaped the LAN boundary.");
            if (NetworkPolicy.IsLanV4(IPAddress.Parse("8.8.8.8")) || NetworkPolicy.IsLanV4(IPAddress.IPv6Any))
                throw new InvalidOperationException("Public address accepted.");
            File.WriteAllLines(Path.Combine(smokeDirectory!, "network-addresses.txt"), available.Select(ip => ip.ToString()));
            foreach (var (protocol, port, label) in new[] { (6, "32145", "TCP"), (17, "32146", "UDP") })
            {
                object rule = FirewallAccess.CreateRule(protocol, port, label);
                try
                {
                    dynamic entry = rule;
                    if (!string.Equals((string)entry.ApplicationName, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase) ||
                        (int)entry.Protocol != protocol || (string)entry.LocalPorts != port ||
                        !string.Equals((string)entry.RemoteAddresses, "LocalSubnet", StringComparison.OrdinalIgnoreCase) ||
                        (int)entry.Direction != 1 || (int)entry.Profiles != 7 || (int)entry.Action != 1 || !(bool)entry.Enabled)
                        throw new InvalidOperationException("Firewall rule escaped the application/subnet/port boundary.");
                }
                finally { System.Runtime.InteropServices.Marshal.ReleaseComObject(rule); }
            }
            var settingsDirectory = Path.Combine(smokeDirectory!, "settings-check");
            var sample = new AppSettings { LaserColor = "#32B894", TrailMs = 800, Language = "en" };
            sample.Save(settingsDirectory);
            if (AppSettings.Load(settingsDirectory).LaserColor != sample.LaserColor) throw new InvalidOperationException("Settings round trip failed.");
            sample.ScreenName = "preserved-display";
            sample.LaserSize = 24;
            sample.Smoothing = "high";
            var general = sample.WithGeneralPreferences("zh", true);
            if (general.Language != "zh" || !general.StartWithWindows || sample.Language != "en" || sample.StartWithWindows ||
                general.LaserColor != sample.LaserColor || general.LaserSize != sample.LaserSize || general.TrailMs != sample.TrailMs ||
                general.Smoothing != sample.Smoothing || general.ScreenName != sample.ScreenName)
                throw new InvalidOperationException("Desktop settings overwrote phone-controlled laser preferences.");
            File.WriteAllText(Path.Combine(settingsDirectory, "settings.json"), "{broken");
            bool corruptRejected = false;
            try { AppSettings.Load(settingsDirectory); } catch (System.Text.Json.JsonException) { corruptRejected = true; }
            if (!corruptRejected || File.ReadAllText(Path.Combine(settingsDirectory, "settings.json")) != "{broken")
                throw new InvalidOperationException("Corrupt settings were overwritten or accepted.");
            using (var icon = Ui.CreateIcon())
            using (var iconFile = File.Create(Path.Combine(smokeDirectory!, "Pointa.ico"))) icon.Save(iconFile);
            var laser = trayMenu!.Items.OfType<System.Windows.Controls.MenuItem>().Single(item => item.IsCheckable);
            laser.IsChecked = false;
            if (overlayEnabled) throw new InvalidOperationException("Tray uncheck did not disable laser.");
            laser.IsChecked = true;
            if (!overlayEnabled) throw new InvalidOperationException("Tray check did not enable laser.");
            await UiSelfCheck.RunAsync(host, trayMenu, smokeDirectory!);
            var beforeMouse = Cursor.Position;
            overlay.MovePointer(.25, .75);
            await Task.Delay(100);
            overlay.HidePointer(true);
            if (Cursor.Position != beforeMouse) throw new InvalidOperationException("Pointer moved the system cursor.");
            File.WriteAllText(Path.Combine(smokeDirectory!, "smoke-result.txt"), "PASS: host started; LAN selection and uninstalled firewall rule scope checked without changing firewall policy; native structs, mapping, foreground policies, overlay styles/focus/mouse/idle/expiry checks passed; settings persistence, corrupt-file preservation and phone effect preservation passed; WPF refresh and save callbacks, language dropdown, checkbox and tray laser toggle checked; official Fluent templates and light/dark Chinese/English windows and popups rendered.");
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
        if (trayMenu is not null) trayMenu.IsOpen = false;
        tray.Icon?.Dispose();
        tray.Dispose();
        overlay.Dispose();
        form.Exiting = true;
        form.Close();
        form.Dispose();
        timer.Dispose();
        W.Application.Current.Shutdown();
    }

    private string T(string zh, string en) => settings.T(zh, en);
}
