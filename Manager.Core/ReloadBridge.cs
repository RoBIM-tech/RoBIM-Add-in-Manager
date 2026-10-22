namespace RoBIM.Manager.Core;

public static class ReloadBridge
{
    public static bool RevitRunning()
    {
        var processes = System.Diagnostics.Process.GetProcessesByName("Revit");
        try { return processes.Length > 0; }
        finally { foreach (var process in processes) process.Dispose(); }
    }
    public static void Install(ManagerPaths paths, string archive)
    {
        var digest = archive + ".sha256";
        if (!File.Exists(digest) || !SafeFiles.Hash(archive).Equals(File.ReadAllText(digest).Trim(), StringComparison.OrdinalIgnoreCase))
            throw new IOException("Reload 연결 패키지 해시가 일치하지 않습니다.");
        var cache = SafeFiles.Child(paths.Data, "BridgePackages/" + Guid.NewGuid().ToString("N"));
        SafeFiles.Extract(archive, cache);
        var service = new InstallService(paths, _ =>
        {
            // First install introduces new scripts only. Replacing a loaded bridge requires closure.
            if (Directory.Exists(paths.Target(Catalog.ReloadBridge))) EnvironmentProbe.EnsureHostClosed(Catalog.Revit);
        });
        service.Install(Catalog.ReloadBridge, new(Path.Combine(cache, Catalog.ReloadBridge.FolderName), "1.0.0-preview.29"));
    }
}
