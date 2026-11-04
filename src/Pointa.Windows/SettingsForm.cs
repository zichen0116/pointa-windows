namespace Pointa.Windows;

internal sealed class SettingsForm : Form
{
    public SettingsForm(AppSettings settings, string diagnostics, Action<AppSettings> save, string? clipboardDiagnostics = null)
    {
        string T(string zh, string en) => settings.T(zh, en);
        Ui.Configure(this, T("映点 · 设置", "Pointa · Settings"), new Size(470, 592));
        MinimizeBox = false;
        var language = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        language.Items.AddRange(["简体中文", "English"]);
        language.SelectedIndex = settings.Language == "zh" ? 0 : 1;
        AddRow(T("语言", "Language"), language, 20);
        var screens = Screen.AllScreens;
        var display = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        display.Items.AddRange(screens.Select((s, i) => $"{i + 1} · {s.Bounds.Width} × {s.Bounds.Height}{(s.Primary ? T(" · 主屏", " · Primary") : "")}").ToArray());
        display.SelectedIndex = Math.Max(0, Array.FindIndex(screens, s => s.DeviceName == settings.ScreenName));
        AddRow(T("激光显示器", "Laser display"), display, 62);
        var startup = new CheckBox { Text = T("登录 Windows 时启动（收进托盘）", "Start with Windows (in the tray)"), Checked = settings.StartWithWindows, AutoSize = true };
        startup.SetBounds(22, 110, 420, 26);
        var publicNetwork = new CheckBox { Text = T("允许公共网络连接（仅在可信 Wi-Fi 启用）", "Allow public networks (trusted Wi-Fi only)"), Checked = settings.AllowPublicNetworks, AutoSize = true };
        publicNetwork.SetBounds(22, 145, 426, 26);
        var networkHint = Ui.Label(T("默认仅私有网络。防火墙未放行或访客 Wi-Fi 隔离时，\n请使用私人热点；更改网络选项会断开当前手机。", "Private networks by default. Firewall rules or guest Wi-Fi\nisolation may block pairing. Network changes disconnect the phone."), 8);
        networkHint.ForeColor = Ui.Muted;
        networkHint.SetBounds(22, 176, 426, 43);
        var colors = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        string[] colorValues = ["#FF6475", "#EF4444", "#32B894", "#4D95ED", "#D69A47", "#B58AE9"];
        colors.Items.AddRange([T("珊瑚红", "Coral"), T("正红", "Red"), T("薄荷绿", "Mint"), T("天空蓝", "Sky"), T("暖金色", "Gold"), T("淡紫色", "Lilac")]);
        var colorIndex = Array.FindIndex(colorValues, x => string.Equals(x, settings.LaserColor, StringComparison.OrdinalIgnoreCase));
        if (colorIndex < 0) { colorValues = [..colorValues, settings.LaserColor]; colors.Items.Add(settings.LaserColor); colorIndex = colorValues.Length - 1; }
        colors.SelectedIndex = colorIndex;
        AddRow(T("激光颜色", "Laser color"), colors, 230);
        var size = new NumericUpDown { Minimum = 6, Maximum = 40, Value = settings.LaserSize };
        AddRow(T("光点大小（DIP）", "Pointer size (DIP)"), size, 272);
        var trail = new NumericUpDown { Minimum = 0, Maximum = 1200, Increment = 50, Value = settings.TrailMs };
        AddRow(T("拖尾（ms，0 为关闭）", "Trail (ms; 0 is off)"), trail, 314);
        var smoothing = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        smoothing.Items.AddRange([T("低 · 更跟手", "Low · responsive"), T("标准", "Standard"), T("高 · 更平滑", "High · smooth")]);
        string[] smoothingValues = ["low", "standard", "high"];
        smoothing.SelectedIndex = Array.IndexOf(smoothingValues, settings.Smoothing);
        AddRow(T("平滑", "Smoothing"), smoothing, 356);
        var advanced = Ui.Label(T("高级信息 · 不含设备码、令牌或私钥", "Diagnostics · No pairing codes, tokens or private keys"), 8, true);
        advanced.SetBounds(22, 402, 426, 26);
        var info = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Text = diagnostics, BackColor = Ui.Surface, BorderStyle = BorderStyle.FixedSingle };
        info.SetBounds(22, 432, 426, 90);
        var copy = Ui.Button(T("复制诊断", "Copy diagnostics"), (_, _) =>
        {
            try { Clipboard.SetText(clipboardDiagnostics ?? diagnostics); }
            catch (System.Runtime.InteropServices.ExternalException) { MessageBox.Show(this, T("剪贴板忙，请重试。", "Clipboard is busy. Please retry.")); }
        });
        copy.SetBounds(22, 542, 100, 32);
        var defaults = Ui.Button(T("恢复效果", "Reset effects"), (_, _) =>
        {
            colors.SelectedIndex = 0;
            size.Value = 16;
            trail.Value = 500;
            smoothing.SelectedIndex = 1;
        });
        defaults.SetBounds(130, 542, 100, 32);
        var cancel = Ui.Button(T("取消", "Cancel"), (_, _) => Close());
        cancel.SetBounds(242, 542, 96, 32);
        var apply = Ui.Button(T("保存", "Save"), (_, _) =>
        {
            var updated = new AppSettings
            {
                Language = language.SelectedIndex == 0 ? "zh" : "en", ScreenName = screens[display.SelectedIndex].DeviceName,
                StartWithWindows = startup.Checked, AllowPublicNetworks = publicNetwork.Checked,
                LaserColor = colorValues[colors.SelectedIndex], LaserSize = (int)size.Value, TrailMs = (int)trail.Value,
                Smoothing = smoothingValues[smoothing.SelectedIndex],
            };
            try { save(updated); DialogResult = DialogResult.OK; Close(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            { MessageBox.Show(this, T("保存失败：", "Could not save: ") + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
        });
        apply.SetBounds(350, 542, 98, 32);
        Controls.AddRange([startup, publicNetwork, networkHint, advanced, info, copy, defaults, cancel, apply]);
        AcceptButton = apply;
        CancelButton = cancel;
        Ui.Finish(this);
    }

    private void AddRow(string name, Control input, int top)
    {
        var label = Ui.Label(name, 9);
        label.SetBounds(22, top, 187, 28);
        input.SetBounds(214, top, 234, 28);
        input.AccessibleName = name;
        Controls.AddRange([label, input]);
    }
}
