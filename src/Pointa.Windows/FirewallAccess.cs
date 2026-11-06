using System.Collections;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Pointa.Windows;

internal static class FirewallAccess
{
    public static async void Request(Form owner, AppSettings settings)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
            { UseShellExecute = true, Verb = "runas", Arguments = "--configure-firewall", WindowStyle = ProcessWindowStyle.Hidden });
            if (process is null) return;
            await process.WaitForExitAsync();
            if (!owner.IsDisposed) MessageBox.Show(owner,
                process.ExitCode == 0 ? settings.T("已允许此程序的局域网端口访问。请在手机上重新连接。", "Pointa's local subnet ports are allowed. Retry on your phone.") :
                    settings.T("无法修改防火墙，请联系管理员检查网络策略。", "Firewall setup failed. Ask your administrator to check network policies."), "Pointa");
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) { }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        { if (!owner.IsDisposed) MessageBox.Show(owner, ex.Message, "Pointa"); }
    }

    public static void ConfigureCurrentExecutable()
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Executable path unavailable.");
        object policy = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2", true)!)!;
        object rules = ((dynamic)policy).Rules;
        try
        {
            var blocked = new List<string>();
            foreach (dynamic existing in (IEnumerable)rules)
            {
                try
                {
                    if ((int)existing.Direction == 1 && (int)existing.Action == 0 &&
                        string.Equals((string?)existing.ApplicationName, executable, StringComparison.OrdinalIgnoreCase)) blocked.Add((string)existing.Name);
                }
                finally { Marshal.ReleaseComObject(existing); }
            }
            foreach (var (protocol, port, label) in new[] { (6, "32145", "TCP"), (17, "32146", "UDP") })
            {
                object rule = CreateRule(protocol, port, label);
                try { ((dynamic)rules).Add(rule); }
                finally { Marshal.ReleaseComObject(rule); }
            }
            // Only this executable's local deny rules are replaced after both narrow allow rules exist.
            foreach (var name in blocked) ((dynamic)rules).Remove(name);
        }
        finally { Marshal.ReleaseComObject(rules); Marshal.ReleaseComObject(policy); }
    }

    internal static object CreateRule(int protocol, string port, string label)
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Executable path unavailable.");
        var identifier = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(executable.ToUpperInvariant())))[..12];
        object rule = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FWRule", true)!)!;
        try
        {
            dynamic entry = rule;
            entry.Name = $"Pointa LAN {label} {identifier}";
            entry.Description = "Pointa encrypted pairing/control. This executable and local subnet only.";
            entry.ApplicationName = executable;
            entry.Protocol = protocol;
            entry.LocalPorts = port;
            entry.RemoteAddresses = "LocalSubnet";
            entry.Direction = 1;
            entry.Profiles = 7;
            entry.Action = 1;
            entry.Enabled = true;
            return rule;
        }
        catch { Marshal.ReleaseComObject(rule); throw; }
    }
}
