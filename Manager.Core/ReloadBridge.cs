namespace RoBIM.Manager.Core;

public static class ReloadBridge
{
    public static bool RevitRunning()
    {
        var processes = System.Diagnostics.Process.GetProcessesByName("Revit");
        try { return processes.Length > 0; }
        finally { foreach (var process in processes) process.Dispose(); }
    }
    public static void Install(ManagerPaths paths, string archive,
        Func<RevitSession?>? currentSession = null, Action<RevitSession>? ensureSession = null, Action? requestReload = null)
    {
        var digest = archive + ".sha256";
        if (!File.Exists(digest) || !SafeFiles.Hash(archive).Equals(File.ReadAllText(digest).Trim(), StringComparison.OrdinalIgnoreCase))
            throw new IOException("Reload 연결 패키지 해시가 일치하지 않습니다.");
        var cache = SafeFiles.Child(paths.Data, "BridgePackages/" + Guid.NewGuid().ToString("N"));
        SafeFiles.Extract(archive, cache);
        var package = new SourcePackage(Path.Combine(cache, Catalog.ReloadBridge.FolderName), "1.0.0-preview.38");
        var session = (currentSession ?? NativeRevitCommands.Session)();
        if (session is not null && Directory.Exists(paths.Target(Catalog.ReloadBridge)))
        {
            // File-delta replacement keeps the loaded root and identical binaries in place.
            // The old handler requests the standard pyRevit Reload before the new code runs.
            if (File.Exists(SafeFiles.Child(paths.Data, "reload-pending.json")) || File.Exists(SafeFiles.Child(paths.Data, "reload-profile.json")))
                throw new IOException("준비된 pyRevit 선택·프로필 변경을 먼저 적용한 뒤 연결을 업데이트하십시오.");
            void Check() { if (ensureSession is not null) ensureSession(session); else RevitReload.EnsureSession(session.Pid, session.StartTicks); }
            new InstallService(paths, _ => Check()).InstallReload(Catalog.ReloadBridge, package);
            Check();
            try { if (requestReload is not null) requestReload(); else new RibbonLayouts(paths).RequestRevit(true); }
            catch (Exception ex) { throw new IOException("연결 파일은 갱신했습니다. 자동 Reload 요청을 보낼 수 없어 Revit의 표준 pyRevit Reload가 필요합니다. " + ex.Message, ex); }
            return;
        }
        var service = new InstallService(paths, _ =>
        {
            // First install introduces new scripts only. Replacing a loaded bridge requires closure.
            if (Directory.Exists(paths.Target(Catalog.ReloadBridge))) EnvironmentProbe.EnsureHostClosed(Catalog.Revit);
        });
        service.Install(Catalog.ReloadBridge, package);
    }
}
