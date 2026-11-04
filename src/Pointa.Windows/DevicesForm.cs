using Pointa.Server;

namespace Pointa.Windows;

internal sealed class DevicesForm : Form
{
    public DevicesForm(AppSettings settings, HostService host)
    {
        string T(string zh, string en) => settings.T(zh, en);
        Ui.Configure(this, T("映点 · 已授权设备", "Pointa · Trusted devices"), new Size(450, 330));
        MinimizeBox = false;
        var list = new ListView { View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false };
        list.Columns.Add(T("设备", "Device"), 175);
        list.Columns.Add(T("最近连接", "Last connected"), 225);
        list.SetBounds(20, 20, 410, 210);
        var hint = Ui.Label(T("撤销后立即断开，需要重新扫码或输入设备码配对。", "Revoking disconnects the device. Pair again to regain access."), 8);
        hint.SetBounds(20, 232, 410, 35);
        hint.ForeColor = Ui.Muted;
        void RefreshDevices()
        {
            list.Items.Clear();
            foreach (var device in host.Devices)
            {
                var item = new ListViewItem(device.Name + (host.ConnectedDevice?.Id == device.Id ? T(" · 在线", " · Online") : "")) { Tag = device.Id };
                item.SubItems.Add(device.LastConnectedAt.ToLocalTime().ToString("g"));
                list.Items.Add(item);
            }
        }
        var revoke = Ui.Button(T("撤销所选设备", "Revoke selected"), (_, _) =>
        {
            if (list.SelectedItems.Count == 0) return;
            var item = list.SelectedItems[0];
            if (MessageBox.Show(this, T($"撤销“{item.Text}”的控制权限？", $"Revoke access for ‘{item.Text}’?"), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try { host.RevokeDevice((string)item.Tag!); RefreshDevices(); }
            catch (IOException ex) { MessageBox.Show(this, ex.Message); }
        });
        revoke.SetBounds(20, 280, 170, 32);
        var clear = Ui.Button(T("清空授权", "Revoke all"), (_, _) =>
        {
            if (MessageBox.Show(this, T("撤销所有手机的控制权限？", "Revoke every paired phone?"), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            try { host.ClearDevices(); RefreshDevices(); }
            catch (IOException ex) { MessageBox.Show(this, ex.Message); }
        });
        clear.SetBounds(260, 280, 170, 32);
        Controls.AddRange([list, hint, revoke, clear]);
        RefreshDevices();
        Ui.Finish(this);
    }
}
