using System.Drawing.Imaging;
using QRCoder;

namespace Pointa.Windows;

internal sealed class PairingForm : Form
{
    private readonly AppSettings settings;
    private readonly Label status, code, expiry, footer, ready, detail, host;
    private readonly PictureBox qr;
    private readonly Button disconnect;
    private string? currentQr;
    private string currentCode = "";
    public event EventHandler? RefreshRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? DevicesRequested;
    public event EventHandler? DisconnectRequested;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool Exiting { get; set; }

    public PairingForm(AppSettings settings)
    {
        this.settings = settings;
        footer = Ui.Label(T("正在启动局域网服务", "Starting local service"), 7.5f);
        Ui.Configure(this, "映点 Pointa", new Size(365, 410));
        MinimizeBox = true;
        status = Ui.Label(T("正在启动…", "Starting…"), 10, true);
        status.SetBounds(20, 17, 325, 26);
        host = Ui.Label(Environment.MachineName, 8);
        host.ForeColor = Ui.Muted;
        host.SetBounds(20, 44, 325, 20);
        qr = new PictureBox { BackColor = Color.White, SizeMode = PictureBoxSizeMode.Zoom, AccessibleName = T("扫描此二维码配对", "Scan this QR code to pair") };
        qr.SetBounds(18, 74, 159, 159);
        qr.Cursor = Cursors.Hand;
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
        var hint = Ui.Label(T("临时设备码", "Pairing code"), 8);
        hint.ForeColor = Ui.Muted;
        hint.SetBounds(193, 83, 153, 22);
        code = Ui.Label("··· ···", 21, true);
        code.SetBounds(190, 107, 161, 45);
        expiry = Ui.Label("", 8);
        expiry.ForeColor = Ui.Muted;
        expiry.SetBounds(193, 152, 155, 22);
        var copy = Ui.Button(T("复制设备码", "Copy code"), (_, _) =>
        {
            if (currentCode.Length == 0) return;
            try { Clipboard.SetText(currentCode); footer.Text = T("已复制设备码", "Code copied"); }
            catch (System.Runtime.InteropServices.ExternalException) { footer.Text = T("剪贴板忙，请重试", "Clipboard busy; retry"); }
        });
        copy.SetBounds(193, 182, 150, 32);
        var card = new Card();
        card.SetBounds(19, 250, 327, 65);
        ready = Ui.Label(T("激光覆盖层已就绪", "Laser overlay ready"), 9, true);
        ready.SetBounds(13, 8, 301, 24);
        detail = Ui.Label(T("支持鼠标穿透 · 无需置顶窗口", "Click-through · Keep your slides focused"), 7.5f);
        detail.ForeColor = Ui.Muted;
        detail.SetBounds(13, 32, 301, 24);
        card.Controls.AddRange([ready, detail]);
        var devices = Ui.Button(T("设备管理", "Devices"), (_, _) => DevicesRequested?.Invoke(this, EventArgs.Empty));
        devices.SetBounds(19, 327, 99, 30);
        var config = Ui.Button(T("设置", "Settings"), (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty));
        config.SetBounds(125, 327, 99, 30);
        disconnect = Ui.Button(T("断开", "Disconnect"), (_, _) => DisconnectRequested?.Invoke(this, EventArgs.Empty));
        disconnect.SetBounds(231, 327, 115, 30);
        disconnect.Enabled = false;
        var line = new Panel { BackColor = ColorTranslator.FromHtml("#E9EDF3") };
        line.SetBounds(0, 370, 365, 1);
        footer.ForeColor = Ui.Muted;
        footer.SetBounds(18, 378, 213, 25);
        var refresh = Ui.Button(T("刷新配对码", "Refresh code"), (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty));
        refresh.SetBounds(235, 377, 111, 27);
        Controls.AddRange([status, host, qr, hint, code, expiry, copy, card, devices, config, disconnect, line, footer, refresh]);
        FormClosing += (_, e) =>
        {
            if (!Exiting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
        };
        Resize += (_, _) => { if (WindowState == FormWindowState.Minimized) { Hide(); WindowState = FormWindowState.Normal; } };
        Ui.Finish(this);
    }

    public void UpdatePairing(string payload, string pairingCode, DateTimeOffset expiresAt, bool lanAvailable, string? connectedName)
    {
        currentCode = pairingCode;
        code.Text = pairingCode.Length == 6 ? pairingCode[..3] + " " + pairingCode[3..] : "··· ···";
        var seconds = Math.Max(0, (int)(expiresAt - DateTimeOffset.UtcNow).TotalSeconds);
        expiry.Text = T($"{seconds / 60:00}:{seconds % 60:00} 后过期", $"Expires in {seconds / 60:00}:{seconds % 60:00}");
        status.Text = connectedName is not null ? T("● 已连接手机", "● Phone connected") :
            lanAvailable ? T("● 等待手机连接", "● Waiting for your phone") : T("● 尚未启用局域网", "● Local network is disabled");
        status.ForeColor = lanAvailable ? Ui.Green : Ui.Ink;
        host.Text = connectedName is not null ? $"{Environment.MachineName}  ·  {connectedName}" : Environment.MachineName;
        disconnect.Enabled = connectedName is not null;
        footer.Text = lanAvailable ? T("局域网 · 加密连接", "Local network · Encrypted") : T("请在设置中选择可信网络", "Enable a trusted network in Settings");
        qr.Visible = lanAvailable;
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
        code.Text = "··· ···";
        currentCode = "";
        expiry.Text = "";
        ready.Text = T("连接尚未就绪", "Connection is not ready");
        detail.Text = message;
        footer.Text = T("请检查设置或重新启动", "Check Settings or restart");
    }

    public void SetOverlayStatus(string message) { ready.Text = message; }

    public void SavePreview(string path)
    {
        using var bitmap = new Bitmap(Width, Height);
        DrawToBitmap(bitmap, new Rectangle(0, 0, Width, Height));
        bitmap.Save(path, ImageFormat.Png);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { qr.Image?.Dispose(); Icon?.Dispose(); }
        base.Dispose(disposing);
    }
    private string T(string zh, string en) => settings.T(zh, en);
}
