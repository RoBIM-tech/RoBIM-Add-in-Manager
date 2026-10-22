using System.IO;

namespace RoBIM.Manager;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var readOnly = args.Any(a => a is "--render-preview" or "--render-cad-buttons" or "--verify-card-ui" or "--inspect-buttons" or "--close-existing" or "--show-existing");
        if (!readOnly)
        {
            try { Core.WindowsDataPath.EnsureDirectory(Core.ManagerPaths.CurrentUser().Data); }
            catch (Exception error)
            {
                if (args.Length == 0) MessageBox.Show(error.Message, "RoBIM Manager 실행 위치 확인", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 1;
            }
        }
        if (args.Length == 2 && args[0] == "--inspect-performance")
        {
            using var check = new ManagerForm(inspect: true);
            check.InspectPerformance(args[1]); Application.Run(check); return 0;
        }
        if (args.Length is 2 or 3 && args[0] == "--inspect-release-offers")
        {
            using var check = new ManagerForm(inspect: true);
            check.InspectInstalledCards(args[1], args.Length == 3 ? args[2] : null, "Revit", refreshOffers: true); Application.Run(check); return 0;
        }
        if (args.Length is 2 or 3 && args[0] == "--inspect-my-tools")
        {
            using var check = new ManagerForm(inspect: true);
            check.InspectInstalledCards(args[1], args.Length == 3 ? args[2] : null, "Mine", refreshOffers: true); Application.Run(check); return 0;
        }
        if (args.Length is 2 or 3 && args[0] == "--inspect-test-card-ui")
        {
            using var check = new ManagerForm(inspect: true);
            check.InspectInstalledCards(args[1], args.Length == 3 ? args[2] : null, "Test"); Application.Run(check); return 0;
        }
        if (args.Length is 2 or 3 && args[0] == "--inspect-ribbon-ui")
        {
            using var check = new ManagerForm(inspect: true);
            check.InspectInstalledCards(args[1], args.Length == 3 ? args[2] : null, "Ribbon"); Application.Run(check); return 0;
        }
        if (args.Length is 2 or 3 && args[0] == "--inspect-installed-card-ui")
        {
            using var check = new ManagerForm(inspect: true);
            check.InspectInstalledCards(args[1], args.Length == 3 ? args[2] : null); Application.Run(check); return 0;
        }
        if (args.Length == 2 && args[0] == "--verify-card-ui")
        {
            try { using var check = new ManagerForm(true); check.VerifyCardUi(args[1]); return 0; }
            catch (Exception error) { File.WriteAllText(args[1], System.Text.Json.JsonSerializer.Serialize(new { Passed = false, Error = error.ToString() })); return 1; }
        }
        if (args.Length == 3 && args[0] == "--inspect-buttons")
        {
            File.WriteAllText(args[2], System.Text.Json.JsonSerializer.Serialize(Core.RevitButtons.Read(args[1]))); return 0;
        }
        if (args.Length == 2 && args[0] == "--import-native-module")
        {
            new Core.NativeRevitCommands(Core.ManagerPaths.CurrentUser()).Import(args[1]); return 0;
        }
        if (args.Contains("--install-native-reload-host"))
        {
            new Core.NativeRevitCommands(Core.ManagerPaths.CurrentUser()).InstallHost(Path.Combine(AppContext.BaseDirectory, "RoBIM-Host-Revit2026.zip"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Autodesk", "Revit", "Addins", "2026")); return 0;
        }
        if (args.Length == 5 && args[0] == "--native-toggle")
        {
            var manager = new Core.NativeRevitCommands(Core.ManagerPaths.CurrentUser());
            var package = manager.Cached(args[1]) ?? throw new IOException("Native module catalog entry missing.");
            var module = RoBIM.Revit.Reload.NativeProtocol.Module(package.Path);
            var tool = new Core.ToolSpec("native-local-" + module.Id, module.Commands[0].Title, Core.ToolKind.NativeRevit, "", "main", Path.GetFileName(package.Path), "독립 모듈", true);
            manager.ToggleWithMigration(tool, package.Path, args[2], bool.Parse(args[3]));
            File.WriteAllText(args[4], System.Text.Json.JsonSerializer.Serialize(manager.Desired())); return 0;
        }
        if (args.Length == 2 && args[0] == "--native-reload")
        {
            var manager = new Core.NativeRevitCommands(Core.ManagerPaths.CurrentUser()); manager.RequestReload();
            File.WriteAllText(args[1], System.Text.Json.JsonSerializer.Serialize(manager.Desired())); return 0;
        }
        if (args.Length == 2 && args[0] == "--render-cad-buttons")
        {
            var catalog = new RoBIM.Cad.Configuration.CadCatalog { Modules = [
                new() { Id = "delhidden", Name = "레이어 정리", Actions = [
                    new() { Id = "cleanup", Title = "숨김 레이어 정리", Panel = "정리", Order = 10 },
                    new() { Id = "preview", Title = "정리 후보 조회", Panel = "정리", Order = 20 }] },
                new() { Id = "ezsplit", Name = "도면 분할", Actions = [new() { Id = "split", Title = "도면 분할", Panel = "도면", Order = 10 }] }] };
            using var dialog = new CadButtonsDialog(catalog, ["delhidden:cleanup", "ezsplit:split"], [new(1234, 1, "AutoCAD 2026 — 예시 도면")]);
            dialog.RenderPreview(args[1]); return 0;
        }
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
            if (args.Contains("--pyrevit")) preview.PreviewPage("PyRevit");
            if (args.Contains("--all")) preview.PreviewPage("All");
            if (args.Contains("--test")) preview.PreviewPage("Test");
            if (args.Contains("--mine")) preview.PreviewPage("Mine");
            if (args.Contains("--ribbon")) preview.PreviewPage("Ribbon");
            if (args.Contains("--locations")) preview.PreviewPage("Locations");
            if (args.Contains("--minimum")) preview.Size = preview.MinimumSize;
            if (args.Contains("--wide")) preview.Size = new System.Drawing.Size(1760, 900);
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
