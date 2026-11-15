using Pointa.Core;
using Pointa.Server;
using W = System.Windows;
using C = System.Windows.Controls;

namespace Pointa.Windows;

internal sealed class SettingsWindow : DesktopWindow
{
    private readonly System.Windows.Threading.DispatcherTimer deviceTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly C.StackPanel deviceList = new();
    private readonly C.TextBlock empty;
    private readonly C.Button clear;
    private readonly Func<HostService?> getHost;
    private readonly AppSettings settings;
    private PairedDevice[] shownDevices = [];
    private string? shownConnected;
    private readonly Action<string, bool, bool> save;
    internal C.ComboBox LanguageSelector { get; }
    internal C.CheckBox Startup { get; }
    internal C.CheckBox AllowPresentationApps { get; }
    internal C.Button SaveButton { get; }
    public bool Saved { get; private set; }

    public SettingsWindow(AppSettings settings, Func<HostService?> getHost, Action<string, bool, bool> save)
        : base(settings.T("映点 · 设置", "Pointa · Settings"), 500)
    {
        this.settings = settings; this.getHost = getHost; this.save = save;
        ResizeMode = W.ResizeMode.NoResize;
        var title = Ui.Text(T("设置", "Settings"), 25, true);
        var subtitle = Ui.Text(T("偏好设置与已授权的手机", "Preferences and your trusted phones"), 13, muted: true);
        subtitle.Margin = new W.Thickness(0, 8, 0, 22);
        LanguageSelector = new C.ComboBox { MinHeight = 36, SelectedIndex = settings.Language == "zh" ? 0 : 1,
            ItemsSource = new[] { "简体中文", "English" } };
        System.Windows.Automation.AutomationProperties.SetName(LanguageSelector, T("语言", "Language"));
        var languageRow = Ui.Row(Ui.Text(T("语言", "Language")), LanguageSelector, 184);
        Startup = new C.CheckBox { Content = T("登录时自动启动，并收进托盘", "Start at login, minimized to tray"),
            IsChecked = settings.StartWithWindows, Margin = new W.Thickness(0, 18, 0, 18), MinHeight = 32 };
        var networkRow = Ui.Row(Ui.Text(T("局域网连接", "Local network")),
            Ui.Button(T("设置防火墙", "Configure firewall"), () => FirewallAccess.Request(this, settings)), 184);
        AllowPresentationApps = new C.CheckBox {
            Content = T("浏览器、PDF 和演示编辑窗口可直接翻页", "Allow browsers, PDFs and presentation editors"),
            IsChecked = settings.AllowPresentationApps, MinHeight = 36, Margin = new W.Thickness(0, 0, 0, 16) };
        var preferences = Ui.Card(Ui.Stack(languageRow, Startup, AllowPresentationApps, networkRow));
        clear = Ui.Button(T("清空授权", "Revoke all"), () =>
        {
            if (Ui.Confirm(this, T("撤销所有手机的控制权限？", "Revoke every paired phone?"))) ChangeDevices(host => host.ClearDevices());
        });
        clear.SetResourceReference(C.Control.ForegroundProperty, "SystemFillColorCriticalBrush");
        var deviceHeader = Ui.Row(Ui.Text(T("已授权设备", "Trusted devices"), 16, true), clear);
        deviceHeader.Margin = new W.Thickness(0, 0, 0, 14);
        empty = Ui.Text(T("还没有已授权设备\n配对后的手机会显示在这里", "No trusted phones yet\nYour paired phones will appear here"), 13, muted: true);
        empty.TextAlignment = W.TextAlignment.Center;
        var listArea = new C.Grid { Height = 136 };
        listArea.Children.Add(new C.ScrollViewer { Content = deviceList, VerticalScrollBarVisibility = C.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = C.ScrollBarVisibility.Disabled });
        listArea.Children.Add(empty);
        var cancel = Ui.Button(T("取消", "Cancel"), Close);
        cancel.IsCancel = true; cancel.MinWidth = 96;
        SaveButton = Ui.Button(T("保存", "Save"), Save, true);
        SaveButton.IsDefault = true; SaveButton.MinWidth = 96;
        var actions = Ui.Row(cancel, SaveButton, 96);
        actions.HorizontalAlignment = W.HorizontalAlignment.Right;
        actions.Margin = new W.Thickness(0, 4, 0, 0);
        Content = Ui.Body(Ui.Stack(title, subtitle, preferences, Ui.Card(Ui.Stack(deviceHeader, listArea)), actions));
        deviceTimer.Tick += (_, _) => RefreshDevices();
        Loaded += (_, _) => { RefreshDevices(); deviceTimer.Start(); };
        Closed += (_, _) => deviceTimer.Stop();
    }

    private void Save()
    {
        try { save(LanguageSelector.SelectedIndex == 0 ? "zh" : "en", Startup.IsChecked == true, AllowPresentationApps.IsChecked == true); Saved = true; Close(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { Ui.Message(this, T("保存失败：", "Could not save: ") + ex.Message); }
    }

    internal void RefreshDevices()
    {
        var host = getHost();
        RenderDevices(host?.Devices.ToArray() ?? [], host?.ConnectedDevice?.Id);
    }

    internal void RenderDevices(PairedDevice[] devices, string? connectedId)
    {
        clear.IsEnabled = devices.Length != 0;
        empty.Visibility = devices.Length == 0 ? W.Visibility.Visible : W.Visibility.Collapsed;
        if (shownConnected == connectedId && shownDevices.SequenceEqual(devices)) return;
        shownDevices = devices; shownConnected = connectedId;
        deviceList.Children.Clear();
        foreach (var device in devices)
        {
            var name = Ui.Text(device.Name, 14, true); name.ToolTip = device.Name;
            var last = Ui.Text(connectedId == device.Id ? T("● 已连接", "● Connected") :
                T("最近连接 · ", "Last connected · ") + device.LastConnectedAt.ToLocalTime().ToString("g"), 12, muted: true);
            if (connectedId == device.Id) last.SetResourceReference(C.TextBlock.ForegroundProperty, "SystemFillColorSuccessBrush");
            last.Margin = new W.Thickness(0, 5, 0, 0);
            var revoke = Ui.Button(T("移除", "Revoke"), () =>
            {
                if (Ui.Confirm(this, T($"撤销“{device.Name}”的控制权限？需要重新配对才能使用。", $"Revoke access for ‘{device.Name}’? Pair again to regain access.")))
                    ChangeDevices(host => host.RevokeDevice(device.Id));
            });
            System.Windows.Automation.AutomationProperties.SetName(revoke, T("撤销设备：", "Revoke device: ") + device.Name);
            var row = Ui.Row(Ui.Stack(name, last), revoke, 90);
            row.Margin = new W.Thickness(0, 0, 8, 16);
            deviceList.Children.Add(row);
        }
    }

    private void ChangeDevices(Action<HostService> change)
    {
        try { var host = getHost(); if (host is not null) change(host); RefreshDevices(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        { Ui.Message(this, T("无法更新设备：", "Could not update devices: ") + ex.Message); }
    }
    private string T(string zh, string en) => settings.T(zh, en);
}
