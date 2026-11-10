using System.Security.Principal;

namespace Pointa.Windows;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Contains("--configure-firewall"))
        {
            try { FirewallAccess.ConfigureCurrentExecutable(); }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException or InvalidOperationException)
            { Environment.ExitCode = 1; }
            return;
        }
        var smokeIndex = Array.IndexOf(args, "--smoke-test");
        var smoke = smokeIndex >= 0 && smokeIndex + 1 < args.Length ? Path.GetFullPath(args[smokeIndex + 1]) : null;
        var data = smoke is null ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pointa") : Path.Combine(smoke, "data");
        var id = smoke is null ? WindowsIdentity.GetCurrent().User!.Value : Guid.NewGuid().ToString("N");
        using var mutex = new Mutex(true, @"Local\Pointa.Instance." + id, out var first);
        using var activate = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\Pointa.Activate." + id);
        using var shutdown = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\Pointa.Shutdown." + id);
        if (!first)
        {
            if (!args.Contains("--shutdown")) { activate.Set(); return; }
            shutdown.Set();
            bool stopped;
            try { stopped = mutex.WaitOne(TimeSpan.FromSeconds(20)); }
            catch (AbandonedMutexException) { stopped = true; }
            if (stopped) mutex.ReleaseMutex();
            else Environment.ExitCode = 2;
            return;
        }
        if (args.Contains("--shutdown")) { mutex.ReleaseMutex(); return; }
        try
        {
            Directory.CreateDirectory(data);
            var settings = AppSettings.Load(data);
            var application = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
            Ui.UseTheme("System");
            System.Windows.Forms.Integration.WindowsFormsHost.EnableWindowsFormsInterop();
            using var context = new PointaContext(data, settings, args.Contains("--background"), smoke);
            var wait = ThreadPool.RegisterWaitForSingleObject(activate, (_, _) => context.Dispatch(context.ShowPairing), null, Timeout.Infinite, false);
            var stopWait = ThreadPool.RegisterWaitForSingleObject(shutdown, (_, _) => context.Dispatch(context.RequestQuit), null, Timeout.Infinite, false);
            try { application.Run(); }
            finally { wait.Unregister(null); stopWait.Unregister(null); }
        }
        catch (Exception ex)
        {
            if (smoke is not null)
            {
                Directory.CreateDirectory(smoke);
                File.WriteAllText(Path.Combine(smoke, "smoke-result.txt"), "FAIL: " + ex);
            }
            else MessageBox.Show("映点启动失败 / Pointa could not start.\n\n" + ex.Message + "\n\n" + data,
                "映点 Pointa", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Environment.ExitCode = 1;
        }
        finally { mutex.ReleaseMutex(); }
    }
}
