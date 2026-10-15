using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace RoBIM.Manager;

internal static class DesktopInstance
{
    private const string EventName = "Local\\RoBIM.Manager.Activate";
    private delegate bool EnumWindow(nint window, nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindow callback, nint parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint window, StringBuilder text, int length);
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(nint window, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] private static extern bool PostMessage(nint window, uint message, nint wparam, nint lparam);

    public static bool SignalExisting()
    {
        try { using var signal = EventWaitHandle.OpenExisting(EventName); signal.Set(); return true; }
        catch (WaitHandleCannotBeOpenedException) { return RestoreLegacy(false); }
    }

    // Recovery for Preview 2, which has no activation event and could start hidden.
    // WM_CLOSE follows the form's normal busy/closing checks; no process is killed.
    public static bool RestoreLegacy(bool close)
    {
        var ids = new HashSet<int>();
        foreach (var process in Process.GetProcessesByName("RoBIM.Manager"))
        {
            using (process)
            {
                if (process.Id == Environment.ProcessId) continue;
                try
                {
                    var path = process.MainModule?.FileName;
                    var installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "RoBIM Manager", "RoBIM.Manager.exe");
                    if (path == installed || path == Environment.ProcessPath) ids.Add(process.Id);
                }
                catch (System.ComponentModel.Win32Exception) { }
            }
        }
        var found = false;
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var pid);
            if (!ids.Contains((int)pid)) return true;
            var title = new StringBuilder(512); GetWindowText(window, title, title.Capacity);
            if (!title.ToString().StartsWith("RoBIM Manager", StringComparison.Ordinal) &&
                !title.ToString().StartsWith("RoBIM Add-in Manager", StringComparison.Ordinal)) return true;
            found = true;
            if (close) PostMessage(window, 0x0010, 0, 0);
            else { ShowWindowAsync(window, 9); SetForegroundWindow(window); }
            return true;
        }, 0);
        return found;
    }

    public static void Run(ManagerForm form)
    {
        using var signal = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
        using var timer = new System.Windows.Forms.Timer { Interval = 200 };
        void Activate()
        {
            form.Show(); form.ShowInTaskbar = true;
            if (form.WindowState == FormWindowState.Minimized) form.WindowState = FormWindowState.Normal;
            ShowWindowAsync(form.Handle, 9); form.BringToFront(); form.Activate(); SetForegroundWindow(form.Handle);
        }
        timer.Tick += (_, _) => { if (signal.WaitOne(0)) Activate(); };
        form.Shown += (_, _) => { Activate(); timer.Start(); };
        Application.Run(form);
    }
}
