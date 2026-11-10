using System.Drawing.Imaging;
using QRCoder;

namespace Pointa.Windows;

internal sealed class PairingForm : Form
{
    private readonly AppSettings settings;
    private readonly Label status, code, expiry, ready, detail, host, offline;
    private readonly PictureBox qr, logo;
    private readonly Button disconnect, copy;
    private readonly LinkLabel address;
    private readonly ToolTip tooltip = new();
    private string? currentAddress, currentQr;
    private string currentCode = "";
    private DateTimeOffset copiedIpUntil, copiedCodeUntil;
    public event EventHandler? RefreshRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? DisconnectRequested;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool Exiting { get; set; }

    public PairingForm(AppSettings settings)
    {
        this.settings = settings;
        Ui.Configure(this, "映点 Pointa", new Size(432, 486));
        MinimizeBox = true;
        using var brand = Ui.CreateIcon(128);
        logo = new PictureBox { Image = brand.ToBitmap(), SizeMode = PictureBoxSizeMode.Zoom, AccessibleName = "Pointa" };
        logo.SetBounds(20, 18, 48, 48);
        var title = Ui.Label("映点 Pointa", 18, true);
        title.SetBounds(78, 18, 330, 32);
        status = Ui.Label(T("正在启动…", "Starting…"), 9.5f);
        status.SetBounds(78, 51, 330, 24);
        host = Ui.Label(Environment.MachineName, 8.5f);
        host.ForeColor = Ui.Muted;
        host.SetBounds(24, 86, 384, 20);
        address = new LinkLabel { Font = new Font("Microsoft YaHei UI", 9), LinkColor = Ui.Ink,
            LinkBehavior = LinkBehavior.HoverUnderline, TextAlign = ContentAlignment.MiddleLeft,
            AccessibleName = T("电脑 IP，点击复制", "Computer IP, click to copy") };
        address.SetBounds(24, 107, 384, 24);
        address.LinkClicked += (_, _) =>
        {
            if (currentAddress is null) return;
            try { Clipboard.SetText(currentAddress); copiedIpUntil = DateTimeOffset.UtcNow.AddSeconds(2); address.Text = T("电脑 IP 已复制", "Computer IP copied"); }
            catch (System.Runtime.InteropServices.ExternalException) { ClipboardBusy(); }
        };
        var pairing = new Card();
        pairing.SetBounds(24, 142, 384, 194);
        offline = Ui.Label(T("连接 Wi-Fi 后\n在这里扫码", "Connect to Wi-Fi\nto show your QR code"), 9);
        offline.TextAlign = ContentAlignment.MiddleCenter;
        offline.ForeColor = Ui.Muted;
        offline.SetBounds(12, 11, 172, 172);
        qr = new PictureBox { BackColor = Color.White, SizeMode = PictureBoxSizeMode.Zoom, Visible = false,
            AccessibleName = T("扫描此二维码配对，点击放大", "Scan to pair; click to enlarge"), Cursor = Cursors.Hand };
        qr.SetBounds(12, 11, 172, 172);
        qr.Click += (_, _) =>
        {
            if (qr.Image is null) return;
            using var zoom = new Form();
            using var zoomImage = new Bitmap(qr.Image);
            Ui.Configure(zoom, T("扫描二维码 · 两分钟内有效", "Scan QR · Valid for two minutes"), new Size(480, 480));
            zoom.Controls.Add(new PictureBox { Dock = DockStyle.Fill, Image = zoomImage, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.White });
            Ui.Finish(zoom);
            zoom.ShowDialog(this);
        };
        var hint = Ui.Label(T("临时设备码", "Pairing code"), 8.5f);
        hint.ForeColor = Ui.Muted;
        hint.SetBounds(206, 25, 162, 22);
        code = Ui.Label("··· ···", 23, true);
        code.Font = new Font("Bahnschrift", 23, FontStyle.Bold);
        code.SetBounds(202, 49, 172, 44);
        expiry = Ui.Label("", 8);
        expiry.ForeColor = Ui.Muted;
        expiry.SetBounds(206, 94, 162, 22);
        copy = Ui.Button(T("复制设备码", "Copy code"), (sender, _) =>
        {
            if (currentCode.Length == 0) return;
            try { Clipboard.SetText(currentCode); copiedCodeUntil = DateTimeOffset.UtcNow.AddSeconds(2); ((Button)sender!).Text = T("已复制", "Copied"); }
            catch (System.Runtime.InteropServices.ExternalException) { ClipboardBusy(); }
        }, primary: true);
        copy.SetBounds(206, 134, 162, 36);
        pairing.Controls.AddRange([offline, qr, hint, code, expiry, copy]);
        qr.BringToFront();

        var card = new Card { Name = "overlay-status" };
        card.SetBounds(24, 352, 384, 64);
        ready = Ui.Label(T("激光覆盖层已就绪", "Laser overlay ready"), 9, true);
        ready.SetBounds(16, 9, 222, 24);
        detail = Ui.Label(T("激光样式在手机端调整", "Adjust laser style on your phone"), 7.5f);
        detail.ForeColor = Ui.Muted;
        detail.SetBounds(16, 34, 222, 20);
        var refresh = Ui.Button(T("刷新配对码", "Refresh code"), (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty));
        refresh.Name = "refresh-pairing";
        refresh.SetBounds(246, 14, 122, 36);
        card.Controls.AddRange([ready, detail, refresh]);
        var config = Ui.Button(T("设置", "Settings"), (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty));
        config.Name = "open-settings";
        config.SetBounds(24, 432, 184, 36);
        disconnect = Ui.Button(T("断开连接", "Disconnect"), (_, _) => DisconnectRequested?.Invoke(this, EventArgs.Empty));
        disconnect.SetBounds(224, 432, 184, 36);
        disconnect.Enabled = false;
        Controls.AddRange([logo, title, status, host, address, pairing, card, config, disconnect]);
        FormClosing += (_, e) => { if (!Exiting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
        Resize += (_, _) => { if (WindowState == FormWindowState.Minimized) { Hide(); WindowState = FormWindowState.Normal; } };
        Ui.Finish(this);
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
        status.ForeColor = lanAvailable ? Ui.Green : Ui.Ink;
        host.Text = connectedName is not null ? $"{Environment.MachineName}  ·  {connectedName}" : Environment.MachineName;
        tooltip.SetToolTip(host, host.Text);
        currentAddress = lanAvailable ? ipAddress : null;
        if (now >= copiedIpUntil) address.Text = currentAddress is not null ? T($"电脑 IP：{currentAddress}  ·  点击复制", $"Computer IP: {currentAddress} · Copy") : T("请连接 Wi-Fi 或有线网络", "Connect to Wi-Fi or Ethernet");
        if (now >= copiedCodeUntil) copy.Text = T("复制设备码", "Copy code");
        address.Enabled = currentAddress is not null;
        copy.Enabled = lanAvailable && currentCode.Length == 6;
        disconnect.Enabled = connectedName is not null;
        qr.Visible = lanAvailable;
        offline.Visible = !lanAvailable;
        if (currentQr == payload) return;
        using var data = QRCodeGenerator.GenerateQrCode(payload, QRCodeGenerator.ECCLevel.M);
        using var encoded = new PngByteQRCode(data);
        using var stream = new MemoryStream(encoded.GetGraphic(6));
        using var source = Image.FromStream(stream);
        var old = qr.Image;
        qr.Image = new Bitmap(source);
        old?.Dispose();
        currentQr = payload;
    }

    public void SetError(string message)
    {
        status.Text = T("服务不可用", "Service unavailable");
        status.ForeColor = Color.Firebrick;
        qr.Visible = false;
        offline.Visible = true;
        offline.Text = T("请检查网络后重试", "Check your network and retry");
        code.Text = "··· ···";
        currentCode = "";
        copy.Enabled = disconnect.Enabled = false;
        expiry.Text = "";
        ready.Text = T("连接尚未就绪", "Connection is not ready");
        detail.Text = message;
        tooltip.SetToolTip(detail, message);
    }

    public void SetOverlayStatus(string message) { ready.Text = message; tooltip.SetToolTip(ready, message); }
    public void SavePreview(string path)
    {
        using var bitmap = new Bitmap(Width, Height);
        DrawToBitmap(bitmap, new Rectangle(0, 0, Width, Height));
        bitmap.Save(path, ImageFormat.Png);
    }
    private void ClipboardBusy() => MessageBox.Show(this, T("剪贴板忙，请重试。", "Clipboard busy; retry."), "Pointa");
    protected override void Dispose(bool disposing)
    {
        if (disposing) { qr.Image?.Dispose(); logo.Image?.Dispose(); tooltip.Dispose(); }
        base.Dispose(disposing);
    }
    private string T(string zh, string en) => settings.T(zh, en);
}
