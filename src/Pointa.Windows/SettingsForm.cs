using Pointa.Core;
using Pointa.Server;

namespace Pointa.Windows;

internal sealed class SettingsForm : Form
{
    private readonly System.Windows.Forms.Timer deviceTimer = new() { Interval = 1000 };
    private readonly FlowLayoutPanel deviceList;
    private readonly Label empty;
    private readonly Button clear;
    private readonly Func<HostService?> getHost;
    private readonly AppSettings settings;
    private PairedDevice[] shownDevices = [];
    private string? shownConnected;

    public SettingsForm(AppSettings settings, Func<HostService?> getHost, Action<string, bool> save)
    {
        this.settings = settings;
        this.getHost = getHost;
        Ui.Configure(this, T("映点 · 设置", "Pointa · Settings"), new Size(480, 576));
        MinimizeBox = false;
        var title = Ui.Label(T("设置", "Settings"), 20, true);
        title.SetBounds(24, 20, 432, 36);
        var subtitle = Ui.Label(T("偏好设置与已授权的手机", "Preferences and your trusted phones"), 9);
        subtitle.ForeColor = Ui.Muted;
        subtitle.SetBounds(24, 60, 432, 24);
        var preferences = new Card();
        preferences.SetBounds(24, 96, 432, 176);
        var languageLabel = Ui.Label(T("语言", "Language"), 9.5f);
        languageLabel.SetBounds(16, 16, 190, 30);
        var language = new ComboBox { Name = "language", DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat, BackColor = Ui.Surface, AccessibleName = T("语言", "Language") };
        language.Items.AddRange(["简体中文", "English"]);
        language.SelectedIndex = settings.Language == "zh" ? 0 : 1;
        language.SetBounds(244, 19, 172, 30);
        var divider = new Panel { BackColor = Ui.Stroke };
        divider.SetBounds(16, 60, 400, 1);
        var startup = new CheckBox { Name = "startup", Text = T("登录时自动启动，并收进托盘", "Start at login, minimized to tray"),
            Checked = settings.StartWithWindows, AutoSize = false, FlatStyle = FlatStyle.Flat, Font = new Font("Microsoft YaHei UI", 9.5f) };
        startup.SetBounds(16, 76, 400, 28);
        var network = Ui.Label(T("局域网连接", "Local network"), 9.5f);
        network.SetBounds(16, 128, 210, 30);
        var firewall = Ui.Button(T("设置防火墙", "Configure firewall"), (_, _) => FirewallAccess.Request(this, settings));
        firewall.SetBounds(244, 124, 172, 36);
        preferences.Controls.AddRange([languageLabel, language, divider, startup, network, firewall]);

        var devices = new Card();
        devices.SetBounds(24, 288, 432, 204);
        var deviceTitle = Ui.Label(T("已授权设备", "Trusted devices"), 11, true);
        deviceTitle.SetBounds(16, 14, 244, 28);
        clear = Ui.Button(T("清空授权", "Revoke all"), (_, _) =>
        {
            if (MessageBox.Show(this, T("撤销所有手机的控制权限？", "Revoke every paired phone?"), Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            ChangeDevices(host => host.ClearDevices());
        });
        clear.ForeColor = Color.FromArgb(166, 53, 65);
        clear.SetBounds(290, 12, 126, 32);
        deviceList = new FlowLayoutPanel { Name = "trusted-devices", AutoScroll = true, WrapContents = false,
            FlowDirection = FlowDirection.TopDown, BackColor = Ui.Surface };
        deviceList.SetBounds(16, 60, 400, 128);
        empty = Ui.Label(T("还没有已授权设备\n配对后的手机会显示在这里", "No trusted phones yet\nYour paired phones will appear here"), 9);
        empty.TextAlign = ContentAlignment.MiddleCenter;
        empty.ForeColor = Ui.Muted;
        empty.SetBounds(16, 60, 400, 128);
        devices.Controls.AddRange([deviceTitle, clear, deviceList, empty]);

        var cancel = Ui.Button(T("取消", "Cancel"), (_, _) => Close());
        cancel.SetBounds(240, 512, 100, 40);
        var apply = Ui.Button(T("保存", "Save"), (_, _) =>
        {
            try { save(language.SelectedIndex == 0 ? "zh" : "en", startup.Checked); DialogResult = DialogResult.OK; Close(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            { MessageBox.Show(this, T("保存失败：", "Could not save: ") + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }, primary: true);
        apply.Name = "save-settings";
        apply.SetBounds(356, 512, 100, 40);
        Controls.AddRange([title, subtitle, preferences, devices, cancel, apply]);
        AcceptButton = apply;
        CancelButton = cancel;
        deviceTimer.Tick += (_, _) => RefreshDevices();
        Shown += (_, _) => { RefreshDevices(); deviceTimer.Start(); };
        Ui.Finish(this);
    }

    internal void RefreshDevices()
    {
        var host = getHost();
        RenderDevices(host?.Devices.ToArray() ?? [], host?.ConnectedDevice?.Id);
    }

    internal void RenderDevices(PairedDevice[] devices, string? connectedId)
    {
        clear.Enabled = devices.Length != 0;
        deviceList.Visible = devices.Length != 0;
        empty.Visible = devices.Length == 0;
        if (shownConnected == connectedId && shownDevices.SequenceEqual(devices)) return;
        shownDevices = devices;
        shownConnected = connectedId;
        deviceList.SuspendLayout();
        while (deviceList.Controls.Count != 0) deviceList.Controls[0].Dispose();
        var scale = DeviceDpi / 96f;
        foreach (var device in devices)
        {
            var row = new Panel { Size = new Size((int)(380 * scale), (int)(58 * scale)), Margin = new Padding(0, 0, 0, (int)(6 * scale)), BackColor = Ui.Surface };
            var name = Ui.Label(device.Name, 9.5f, true);
            name.SetBounds(0, 0, (int)(278 * scale), (int)(26 * scale));
            var last = Ui.Label(connectedId == device.Id ? T("● 已连接", "● Connected") :
                T("最近连接 · ", "Last connected · ") + device.LastConnectedAt.ToLocalTime().ToString("g"), 7.5f);
            last.ForeColor = connectedId == device.Id ? Ui.Green : Ui.Muted;
            last.SetBounds(0, (int)(28 * scale), (int)(278 * scale), (int)(22 * scale));
            var revoke = Ui.Button(T("移除", "Revoke"), (_, _) =>
            {
                if (MessageBox.Show(this, T($"撤销“{device.Name}”的控制权限？需要重新配对才能使用。", $"Revoke access for ‘{device.Name}’? Pair again to regain access."), Text,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                ChangeDevices(host => host.RevokeDevice(device.Id));
            });
            revoke.AccessibleName = T("撤销设备：", "Revoke device: ") + device.Name;
            revoke.SetBounds((int)(288 * scale), (int)(7 * scale), (int)(92 * scale), (int)(36 * scale));
            row.Controls.AddRange([name, last, revoke]);
            deviceList.Controls.Add(row);
        }
        deviceList.ResumeLayout(true);
    }

    private void ChangeDevices(Action<HostService> change)
    {
        try { var host = getHost(); if (host is not null) change(host); RefreshDevices(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        { MessageBox.Show(this, T("无法更新设备：", "Could not update devices: ") + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) deviceTimer.Dispose();
        base.Dispose(disposing);
    }
    private string T(string zh, string en) => settings.T(zh, en);
}
