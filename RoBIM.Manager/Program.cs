using System.IO;

namespace RoBIM.Manager;

internal static class Program
{
    private static void RecordUiFailure(string source, Exception error)
    {
        // Crash diagnostics remain on the local computer, never uploaded by CI.
        try
        {
            var root = Core.ManagerPaths.CurrentUser().Data;
            Directory.CreateDirectory(root);
            var log = Path.Combine(root, "cad-panel-errors.log");
            if (File.Exists(log) && new FileInfo(log).Length > 1024 * 1024)
                File.Move(log, log + ".old", true);
            File.AppendAllText(log, $"[{DateTimeOffset.Now:O}] {source}: {error}{Environment.NewLine}");
        }
        catch (Exception) { /* Crash handling must not cause another failure. */ }
    }
    [STAThread]
    private static int Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            RecordUiFailure("unhandled", e.ExceptionObject as Exception ?? new Exception(Convert.ToString(e.ExceptionObject)));
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            RecordUiFailure("task", e.Exception);
            e.SetObserved();
        };
        if (args.Length == 2 && args[0] == "--apply-manager-update") return Core.ManagerUpdates.Apply(args[1]);
        if (args.Length == 2 && args[0] == "--verify-setup-runtime")
        {
            var reports = new List<string> { "Manager: " + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription + " / x64=" + Environment.Is64BitProcess };
            var ready = Environment.Is64BitProcess && Environment.Version.Major == 10;
            foreach (var cad in new[] { false, true })
            {
                var config = Path.Combine(Core.HostRequirements.HostRoot(cad),cad ? "acdbmgd.runtimeconfig.json" : "RevitNET.runtimeconfig.json");
                if (!File.Exists(config)) continue;
                var check=Core.HostRequirements.Check(cad); reports.Add(check.Text);
                using var json=System.Text.Json.JsonDocument.Parse(File.ReadAllText(config));
                if (json.RootElement.GetProperty("runtimeOptions").GetProperty("tfm").GetString()=="net10.0") ready &= check.Ready;
            }
            File.WriteAllText(args[1],string.Join("\n\n",reports)); return ready ? 0 : 1;
        }

        if (args.Length is 1 or 2 && args[0] is "--uninstall-user-data" or "--uninstall-user-data-silent")
        {
            var silentCleanup = args[0] == "--uninstall-user-data-silent";
            var keepSettings = args.Contains("--keep-settings");
            try
            {
                if (args.Length == 2 && args[1] != "--keep-settings") return 1;
                if (!silentCleanup && !keepSettings)
                {
                    var delete = new System.Windows.Forms.TaskDialogButton("개인 설정 삭제");
                    var keep = new System.Windows.Forms.TaskDialogButton("개인 설정 유지");
                    var cancel = System.Windows.Forms.TaskDialogButton.Cancel;
                    var choice = System.Windows.Forms.TaskDialog.ShowDialog(new System.Windows.Forms.TaskDialogPage
                    {
                        Caption = "RoBIM 제거", Heading = "개인 설정을 어떻게 할까요?",
                        Text = "설치한 도구·연결 모듈·검색 기록·다운로드 캐시·복구 백업은 제거합니다.\n\n유지를 선택하면 계정 연결·검색 대상·리본 배치·설치 위치 설정을 남겨 재설치할 때 사용합니다.",
                        Buttons = { delete, keep, cancel }, DefaultButton = keep,
                        AllowCancel = true
                    });
                    if (choice != delete && choice != keep) return 1;
                    keepSettings = choice == keep;
                }
                var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                void WaitForHosts()
                {
                    while (true)
                    {
                        try
                        {
                            var managers = System.Diagnostics.Process.GetProcessesByName("RoBIM.Manager");
                            try
                            {
                                if (managers.Any(process => process.Id != Environment.ProcessId))
                                    throw new InvalidOperationException("RoBIM Manager가 실행 중입니다. 열린 Manager 창을 종료하십시오.");
                            }
                            finally { foreach (var manager in managers) manager.Dispose(); }
                            Core.EnvironmentProbe.EnsureHostClosed(Core.Catalog.RevitHost);
                            Core.EnvironmentProbe.EnsureHostClosed(Core.Catalog.Cad);
                            return;
                        }
                        catch (InvalidOperationException error)
                        {
                            if (silentCleanup) throw;
                            if (System.Windows.MessageBox.Show(error.Message + "\n\n종료한 뒤 재시도를 누르면 제거를 계속합니다.",
                                "RoBIM 제거 — 실행 중인 프로그램", System.Windows.MessageBoxButton.OKCancel, System.Windows.MessageBoxImage.Warning) != System.Windows.MessageBoxResult.OK)
                                throw new OperationCanceledException();
                        }
                    }
                }
                new Core.ManagerUninstall(Core.ManagerPaths.CurrentUser(),
                    Path.Combine(roaming, "Autodesk", "Revit", "Addins"),
                    Path.Combine(roaming, "pyRevit", "pyRevit_config.ini"), WaitForHosts).Run(keepSettings);
                if (Core.StartupRegistration.Enabled(Environment.ProcessPath!)) Core.StartupRegistration.Set(Environment.ProcessPath!, false);
                return 0;
            }
            catch (OperationCanceledException) { return 1; }
            catch (Exception error)
            {
                if (!silentCleanup) System.Windows.MessageBox.Show("RoBIM 전체 제거를 완료하지 못했습니다. Manager는 유지합니다.\n\n" + error.Message,
                    "RoBIM 제거", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return 1;
            }
        }
        var readOnly = args.Any(a => a is "--render-preview" or "--render-cad-buttons" or "--verify-card-ui" or "--verify-team-ui" or "--verify-account-ui" or "--inspect-buttons" or "--close-existing" or "--show-existing");
        if (!readOnly)
        {
            try { Core.WindowsDataPath.EnsureDirectory(Core.ManagerPaths.CurrentUser().Data); }
            catch (Exception error)
            {
                if (args.Length == 0) System.Windows.MessageBox.Show(error.Message, "RoBIM Manager 실행 위치 확인", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return 1;
            }
        }
        if (args.Length == 3 && args[0] == "--inspect-buttons")
        {
            File.WriteAllText(args[2], System.Text.Json.JsonSerializer.Serialize(Core.RevitButtons.Read(args[1]))); return 0;
        }
        if (args.Length == 2 && args[0] == "--import-native-module")
        {
            using var permission = new Core.TeamAccess(serve: false); permission.Refresh().GetAwaiter().GetResult();
            new Core.NativeRevitCommands(Core.ManagerPaths.CurrentUser(), requireChannel: permission.Require).Import(args[1]); return 0;
        }
        if (args.Contains("--install-native-reload-host"))
        {
            System.Windows.MessageBox.Show("프로그램 로더 화면에서 저장소의 Revit 로더를 선택하여 적용하십시오.", "RoBIM Manager"); return 1;
        }
        if (args.Length == 5 && args[0] == "--native-toggle")
        {
            using var permission = new Core.TeamAccess(serve: false); permission.Refresh().GetAwaiter().GetResult();
            var manager = new Core.NativeRevitCommands(Core.ManagerPaths.CurrentUser(), requireChannel: permission.Require);
            var package = manager.Cached(args[1]) ?? throw new IOException("Native module catalog entry missing.");
            var module = RoBIM.Revit.Reload.NativeProtocol.Module(package.Path);
            var tool = new Core.ToolSpec("native-local-" + module.Id, module.Commands[0].Title, Core.ToolKind.NativeRevit, "", "main", Path.GetFileName(package.Path), "도구 모듈", true);
            manager.ToggleWithMigration(tool, package.Path, args[2], bool.Parse(args[3]));
            File.WriteAllText(args[4], System.Text.Json.JsonSerializer.Serialize(manager.Desired())); return 0;
        }
        if (args.Length == 2 && args[0] == "--native-reload")
        {
            var manager = new Core.NativeRevitCommands(Core.ManagerPaths.CurrentUser()); manager.RequestReload(includeCachedUpdates: false);
            File.WriteAllText(args[1], System.Text.Json.JsonSerializer.Serialize(manager.Desired())); return 0;
        }
        if (args.Contains("--close-existing")) { DesktopInstance.RestoreLegacy(true); return 0; }
        if (args.Contains("--show-existing")) { return DesktopInstance.SignalExisting() ? 0 : 1; }
        // Historical bridge commands are deliberately disabled: pyRevit
        // repositories remain archived but no installation or legacy apply is allowed.
        if (args.Contains("--install-reload-bridge") || args.Contains("--apply-revit-reload"))
            return 1;
        if (args.Contains("--render-preview"))
        {
            var destination = args.Last();
            using var previewBackend = new ManagerBackend(preview: true);
            var previewApp = System.Windows.Application.Current ?? new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
            var previewWin = new Manager2Window(previewBackend)
            {
                ShowInTaskbar = false,
                WindowStartupLocation = System.Windows.WindowStartupLocation.Manual,
                Left = -20000,
                Top = -20000
            };
            previewWin.Loaded += (_, _) =>
            {
                previewWin.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, () =>
                {
                    previewWin.UpdateLayout();
                    var width = Math.Max(1, (int)Math.Ceiling(previewWin.ActualWidth));
                    var height = Math.Max(1, (int)Math.Ceiling(previewWin.ActualHeight));
                    var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                    rtb.Render(previewWin);
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
                    using (var fs = File.Create(destination)) encoder.Save(fs);
                    previewApp.Shutdown();
                });
            };
            previewWin.Show();
            previewApp.Run();
            return 0;
        }
        using var instance = new Mutex(true, "Local\\RoBIM.Manager.Desktop", out var created);
        if (!created)
        {
            if (!DesktopInstance.SignalExisting())
                System.Windows.MessageBox.Show("기존 RoBIM Manager가 시작 중입니다. 잠시 후 다시 실행하십시오.", "RoBIM Manager");
            return 0;
        }
        using var backend = new ManagerBackend();
        var wpfApp = System.Windows.Application.Current ?? new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
        wpfApp.DispatcherUnhandledException += (_, e) =>
        {
            RecordUiFailure("wpf", e.Exception);
            try { System.Windows.MessageBox.Show("RoBIM Manager 화면 오류가 발생했습니다. Manager 데이터 폴더의 cad-panel-errors.log를 확인하세요.\n\n" + e.Exception.Message, "RoBIM Manager"); }
            catch (Exception) { }
            e.Handled = true;
        };
        var modern = new Manager2Window(backend);
        using var activateSignal = new EventWaitHandle(false, EventResetMode.AutoReset, DesktopInstance.ActivationEventName);
        var waitRegistration = ThreadPool.RegisterWaitForSingleObject(
            activateSignal,
            (_, _) => wpfApp.Dispatcher.BeginInvoke(modern.ActivateFromSingleInstance),
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);
        try
        {
            backend.BackendClosed += () => wpfApp.Dispatcher.BeginInvoke(() => wpfApp.Shutdown());
            wpfApp.MainWindow = modern;
            if (args.Contains("--background")) _ = backend.StartHiddenBackendAsync();
            else modern.Show();
            wpfApp.Run();
        }
        finally
        {
            waitRegistration.Unregister(null);
        }
        return 0;
    }
}
