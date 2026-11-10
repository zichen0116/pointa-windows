using QRCoder;
using W = System.Windows;
using C = System.Windows.Controls;

namespace Pointa.Windows;

internal sealed class PairingWindow : DesktopWindow
{
    private readonly AppSettings settings;
    private readonly C.TextBlock status, host, code, expiry, ready, detail, offline;
    private readonly C.Image qr;
    private readonly C.Button address, copy, disconnect, qrButton;
    private string? currentAddress, currentQr;
    private string currentCode = "";
    private DateTimeOffset copiedIpUntil, copiedCodeUntil;
    public event EventHandler? RefreshRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? DisconnectRequested;
    internal C.Button RefreshButton { get; }
    internal C.Button SettingsButton { get; }

    public PairingWindow(AppSettings settings) : base("映点 Pointa", 460)
    {
        this.settings = settings;
        status = Ui.Text(T("正在启动…", "Starting…"), 13);
        var heading = Ui.Stack(Ui.Text("映点 Pointa", 25, true), status);
        heading.Margin = new W.Thickness(16, 0, 0, 0);
        var brand = new C.DockPanel { Margin = new W.Thickness(0, 0, 0, 20) };
        brand.Children.Add(new C.Image { Source = Ui.Logo(), Width = 48, Height = 48 }); brand.Children.Add(heading);
        host = Ui.Text(Environment.MachineName, 12, muted: true);
        address = Ui.Button(T("请连接 Wi-Fi 或有线网络", "Connect to Wi-Fi or Ethernet"), () =>
        {
            if (currentAddress is null) return;
            try { W.Clipboard.SetText(currentAddress); copiedIpUntil = DateTimeOffset.UtcNow.AddSeconds(2); address!.Content = T("电脑 IP 已复制", "Computer IP copied"); }
            catch (System.Runtime.InteropServices.ExternalException) { ClipboardBusy(); }
        });
        address.HorizontalAlignment = W.HorizontalAlignment.Left;
        System.Windows.Automation.AutomationProperties.SetName(address, T("复制电脑 IP", "Copy computer IP"));
        address.Margin = new W.Thickness(0, 8, 0, 16);
        qr = new C.Image { Width = 176, Height = 176, Stretch = System.Windows.Media.Stretch.Uniform };
        System.Windows.Media.RenderOptions.SetBitmapScalingMode(qr, System.Windows.Media.BitmapScalingMode.NearestNeighbor);
        qrButton = Ui.Button("", ShowQr);
        qrButton.Visibility = W.Visibility.Hidden;
        qrButton.Content = qr; qrButton.Padding = new W.Thickness(0); qrButton.Background = System.Windows.Media.Brushes.White;
        System.Windows.Automation.AutomationProperties.SetName(qrButton, T("扫描二维码，点击放大", "Scan QR; click to enlarge"));
        offline = Ui.Text(T("连接 Wi-Fi 后\n在这里扫码", "Connect to Wi-Fi\nto show your QR code"), 13, muted: true);
        offline.TextAlignment = W.TextAlignment.Center;
        var qrArea = new C.Grid { Width = 176, Height = 176 };
        qrArea.Children.Add(qrButton); qrArea.Children.Add(offline);
        code = Ui.Text("··· ···", 30, true);
        code.FontFamily = new System.Windows.Media.FontFamily("Bahnschrift");
        code.Margin = new W.Thickness(0, 8, 0, 6);
        expiry = Ui.Text("", 12, muted: true);
        copy = Ui.Button(T("复制设备码", "Copy code"), () =>
        {
            if (currentCode.Length != 6) return;
            try { W.Clipboard.SetText(currentCode); copiedCodeUntil = DateTimeOffset.UtcNow.AddSeconds(2); copy!.Content = T("已复制", "Copied"); }
            catch (System.Runtime.InteropServices.ExternalException) { ClipboardBusy(); }
        }, true);
        copy.Margin = new W.Thickness(0, 18, 0, 0);
        var pairing = Ui.Row(qrArea, Ui.Stack(Ui.Text(T("临时设备码", "Pairing code"), 12, muted: true), code, expiry, copy), 168);
        ready = Ui.Text(T("激光覆盖层已就绪", "Laser overlay ready"), 13, true);
        detail = Ui.Text(T("激光样式在手机端调整", "Adjust laser style on your phone"), 11, muted: true);
        detail.Margin = new W.Thickness(0, 6, 0, 0);
        RefreshButton = Ui.Button(T("刷新配对码", "Refresh code"), () => RefreshRequested?.Invoke(this, EventArgs.Empty));
        RefreshButton.Name = "RefreshPairing";
        var overlay = Ui.Row(Ui.Stack(ready, detail), RefreshButton, 126);
        SettingsButton = Ui.Button(T("设置", "Settings"), () => SettingsRequested?.Invoke(this, EventArgs.Empty));
        disconnect = Ui.Button(T("断开连接", "Disconnect"), () => DisconnectRequested?.Invoke(this, EventArgs.Empty));
        disconnect.IsEnabled = false;
        Content = Ui.Body(Ui.Stack(brand, host, address, Ui.Card(pairing), Ui.Card(overlay), Ui.Row(SettingsButton, disconnect, 192)));
        Closing += (_, e) => { if (!Exiting) { e.Cancel = true; Hide(); } };
        StateChanged += (_, _) => { if (WindowState == W.WindowState.Minimized) { Hide(); WindowState = W.WindowState.Normal; } };
    }

    public void UpdatePairing(string payload, string pairingCode, DateTimeOffset expiresAt, bool lanAvailable, string? connectedName, string? ipAddress = null)
    {
        currentCode = pairingCode;
        code.Text = pairingCode.Length == 6 ? pairingCode[..3] + " " + pairingCode[3..] : "··· ···";
        var now = DateTimeOffset.UtcNow;
        var seconds = Math.Max(0, (int)(expiresAt - now).TotalSeconds);
        expiry.Text = T($"{seconds / 60:00}:{seconds % 60:00} 后过期", $"Expires in {seconds / 60:00}:{seconds % 60:00}");
        status.Text = connectedName is not null ? T("● 已连接手机", "● Phone connected") :
            lanAvailable ? T("● 等待手机连接", "● Waiting for your phone") : T("● 未找到局域网地址", "● No local network address");
        status.SetResourceReference(C.TextBlock.ForegroundProperty, lanAvailable ? "SystemFillColorSuccessBrush" : "TextFillColorPrimaryBrush");
        host.Text = connectedName is not null ? $"{Environment.MachineName}  ·  {connectedName}" : Environment.MachineName;
        host.ToolTip = host.Text;
        currentAddress = lanAvailable ? ipAddress : null;
        if (now >= copiedIpUntil) address.Content = currentAddress is not null ? T($"电脑 IP：{currentAddress}  ·  复制", $"Computer IP: {currentAddress} · Copy") : T("请连接 Wi-Fi 或有线网络", "Connect to Wi-Fi or Ethernet");
        if (now >= copiedCodeUntil) copy.Content = T("复制设备码", "Copy code");
        address.IsEnabled = currentAddress is not null;
        copy.IsEnabled = lanAvailable && currentCode.Length == 6;
        disconnect.IsEnabled = connectedName is not null;
        qr.Visibility = lanAvailable ? W.Visibility.Visible : W.Visibility.Hidden;
        qrButton.Visibility = qr.Visibility;
        offline.Visibility = lanAvailable ? W.Visibility.Collapsed : W.Visibility.Visible;
        if (currentQr == payload) return;
        using var data = QRCodeGenerator.GenerateQrCode(payload, QRCodeGenerator.ECCLevel.M);
        using var encoded = new PngByteQRCode(data);
        qr.Source = Ui.Png(encoded.GetGraphic(6));
        currentQr = payload;
    }

    private void ShowQr()
    {
        if (qr.Visibility != W.Visibility.Visible || qr.Source is null) return;
        using var zoom = new DesktopWindow(T("扫描二维码 · 两分钟内有效", "Scan QR · Valid for two minutes"), 520) { Owner = this };
        zoom.Content = new C.Border { Padding = new W.Thickness(20), Background = System.Windows.Media.Brushes.White,
            Child = new C.Image { Source = qr.Source, Width = 480, Height = 480 } };
        zoom.ShowDialog();
    }
    public void SetError(string message)
    {
        status.Text = T("服务不可用", "Service unavailable");
        status.SetResourceReference(C.TextBlock.ForegroundProperty, "SystemFillColorCriticalBrush");
        qr.Visibility = W.Visibility.Hidden; offline.Visibility = W.Visibility.Visible;
        qrButton.Visibility = W.Visibility.Hidden;
        offline.Text = T("请检查网络后重试", "Check your network and retry");
        code.Text = "··· ···"; currentCode = ""; copy.IsEnabled = disconnect.IsEnabled = false; expiry.Text = "";
        ready.Text = T("连接尚未就绪", "Connection is not ready"); detail.Text = message; detail.ToolTip = message;
    }
    public void SetOverlayStatus(string message) { ready.Text = message; ready.ToolTip = message; }
    public void SavePreview(string path) => Ui.SavePreview((W.FrameworkElement)Content, path);
    private void ClipboardBusy() => Ui.Message(this, T("剪贴板忙，请重试。", "Clipboard busy; retry."));
    private string T(string zh, string en) => settings.T(zh, en);
}
