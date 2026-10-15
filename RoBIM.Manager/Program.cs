using System.IO;

namespace RoBIM.Manager;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Contains("--close-existing")) { DesktopInstance.RestoreLegacy(true); return 0; }
        if (args.Contains("--show-existing")) { return DesktopInstance.SignalExisting() ? 0 : 1; }
        if (args.Contains("--install-reload-bridge"))
        {
            try
            {
                _ = Core.EnvironmentProbe.RevitRoots().GetAwaiter().GetResult();
                Core.ReloadBridge.Install(Core.ManagerPaths.CurrentUser(), Path.Combine(AppContext.BaseDirectory, "RoBIM-ReloadBridge.zip"));
                return 0;
            }
            catch { return 1; }
        }
        if (args.Length == 3 && args[0] == "--apply-revit-reload")
        {
            var paths = Core.ManagerPaths.CurrentUser();
            try
            {
                var pid = int.Parse(args[1]); var ticks = long.Parse(args[2]);
                new Core.RevitReload(paths).Apply(() => Core.RevitReload.EnsureSession(pid, ticks)).GetAwaiter().GetResult();
                File.WriteAllText(Core.SafeFiles.Child(paths.Data, "reload-result.json"), System.Text.Json.JsonSerializer.Serialize(new { Success = true, Message = "준비된 변경을 적용했습니다." }));
                return 0;
            }
            catch (Exception ex)
            {
                Directory.CreateDirectory(paths.Data);
                File.WriteAllText(Core.SafeFiles.Child(paths.Data, "reload-result.json"), System.Text.Json.JsonSerializer.Serialize(new { Success = false, Message = ex.Message }));
                return 1;
            }
        }
        if (args.Contains("--render-preview"))
        {
            using var preview = new ManagerForm(true);
            if (args.Contains("--developer")) preview.PreviewPage("Developer");
            if (args.Contains("--account")) preview.PreviewPage("Account");
            if (args.Contains("--extensions")) preview.PreviewPage("Extensions");
            if (args.Contains("--cad")) preview.PreviewPage("Cad");
            preview.RenderPreview(args.Last());
            return 0;
        }
        using var instance = new Mutex(true, "Local\\RoBIM.Manager.Desktop", out var created);
        if (!created)
        {
            if (!DesktopInstance.SignalExisting())
                MessageBox.Show("기존 RoBIM Manager가 시작 중입니다. 잠시 후 다시 실행하십시오.", "RoBIM Manager");
            return 0;
        }
        DesktopInstance.Run(new ManagerForm());
        return 0;
    }
}
