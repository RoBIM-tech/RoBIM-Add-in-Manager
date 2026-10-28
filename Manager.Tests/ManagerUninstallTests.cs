using System.Text.Json;
using RoBIM.Manager.Core;

internal static class ManagerUninstallTests
{
    public static void Run(string root, Action<string, Action> test)
    {
        (ManagerPaths Paths, string Addins, string Config) Fixture()
        {
            var dir = Path.Combine(root, "uninstall-" + Guid.NewGuid().ToString("N"));
            var paths = new ManagerPaths(Path.Combine(dir, "data"), Path.Combine(dir, "cad"), Path.Combine(dir, "extensions"));
            Directory.CreateDirectory(paths.Data);
            return (paths, Path.Combine(dir, "addins"), Path.Combine(dir, "pyrevit.ini"));
        }
        void Require(bool value, string reason) { if (!value) throw new Exception(reason); }
        void Reject(Action action) { try { action(); } catch (IOException) { return; } catch (InvalidOperationException) { return; } throw new Exception("Expected rejection"); }
        void Record(ManagerPaths paths, string id, string target)
        {
            Directory.CreateDirectory(target); File.WriteAllText(Path.Combine(target, "owned.txt"), "owned");
            var records = new InstallService(paths).ReadState();
            records[id] = new(id, "test", target, DateTimeOffset.UtcNow, SafeFiles.Hashes(target));
            File.WriteAllText(Path.Combine(paths.Data, "installed.json"), JsonSerializer.Serialize(records));
        }
        test("UninstallRemovesOwnedToolsDataAndCredentialsPreservesOtherProducts", () =>
        {
            var f = Fixture();
            var cad = Path.Combine(f.Paths.CadPlugins, "Example.bundle"); Record(f.Paths, "example", cad);
            var py = Path.Combine(f.Paths.RevitExtensions, "RoBIM-Selected.extension"); Record(f.Paths, Catalog.RevitSelected.Id, py);
            var external = Path.Combine(f.Paths.CadPlugins, "Other.bundle"); Directory.CreateDirectory(external); File.WriteAllText(Path.Combine(external, "keep"), "other");
            File.WriteAllText(f.Config, "[core]\r\nuserextensions = " + JsonSerializer.Serialize(new[] { py, external }) + "\r\n[RoBIM-Selected]\r\ndisabled = false\r\n[Other]\r\nvalue = keep\r\n");
            Directory.CreateDirectory(Path.Combine(f.Paths.Data, "NativeRevit", "Modules"));
            File.WriteAllText(Path.Combine(f.Paths.Data, "discovered-repositories.json"), "old owner");
            var cleared = false;
            new ManagerUninstall(f.Paths, f.Addins, f.Config, () => { }, () => cleared = true).Run();
            Require(cleared && !Directory.Exists(f.Paths.Data) && !Directory.Exists(cad) && !Directory.Exists(py), "Owned data remained");
            Require(File.Exists(Path.Combine(external, "keep")), "Other product removed");
            Require(File.ReadAllText(f.Config).Contains("[Other]\r\nvalue = keep") && !File.ReadAllText(f.Config).Contains("[RoBIM-Selected]"), "Shared config not cleaned precisely");
        });
        test("UninstallPreflightRejectsModifiedToolBeforeAnyRemoval", () =>
        {
            var f = Fixture(); var a = Path.Combine(f.Paths.CadPlugins, "A.bundle"); var b = Path.Combine(f.Paths.CadPlugins, "B.bundle");
            Record(f.Paths, "a", a); Record(f.Paths, "b", b); File.WriteAllText(Path.Combine(b, "owned.txt"), "user edit");
            Reject(() => new ManagerUninstall(f.Paths, f.Addins, f.Config, () => { }, () => throw new Exception("Credentials should remain")).Run());
            Require(Directory.Exists(a) && Directory.Exists(b) && File.Exists(Path.Combine(f.Paths.Data, "installed.json")), "Preflight modified installation");
        });
        test("UninstallKeepSettingsPreservesPreferencesButClearsOldRepositoryCache", () =>
        {
            var f = Fixture();
            var settings = new[] { "github-auth.json", "repository-publishers.json", "ribbon-layout-revit.json", "ribbon-layout-autocad.json", "pyrevit-location.json", "revit-mode.json" };
            foreach (var file in settings.Where(x => x != "pyrevit-location.json")) File.WriteAllText(Path.Combine(f.Paths.Data, file), "{}");
            File.WriteAllText(Path.Combine(f.Paths.Data, "pyrevit-location.json"), JsonSerializer.Serialize(new PyRevitLocationSettings(1, f.Paths.RevitExtensions, [])));
            foreach (var file in new[] { "discovered-repositories.json", "extension-repositories.json", "manager-display-cache.json" }) File.WriteAllText(Path.Combine(f.Paths.Data, file), "old owner");
            var target = Path.Combine(f.Paths.CadPlugins, "Owned.bundle"); Record(f.Paths, "owned", target);
            var cleared = false;
            new ManagerUninstall(f.Paths, f.Addins, f.Config, () => { }, () => cleared = true).Run(true);
            Require(!cleared && !Directory.Exists(target), "Keep settings kept tool or deleted credentials");
            Require(Directory.GetFiles(f.Paths.Data).Length == settings.Length && settings.All(x => File.Exists(Path.Combine(f.Paths.Data, x))), "Preferences lost or stale discovery remained");
            new ManagerUninstall(f.Paths, f.Addins, f.Config, () => { }, () => cleared = true).Run();
            Require(cleared && !Directory.Exists(f.Paths.Data), "Full reset after keeping settings failed");
        });
        test("UninstallRejectsEscapedInstallRecord", () =>
        {
            var f = Fixture(); var external = Path.Combine(Path.GetDirectoryName(f.Paths.Data)!, "personal.bundle"); Record(f.Paths, "bad", external);
            Reject(() => new ManagerUninstall(f.Paths, f.Addins, f.Config, () => { }, () => { }).Run());
            Require(File.Exists(Path.Combine(external, "owned.txt")), "Outside files removed");
        });
        test("UninstallRunningHostBlocksBeforeMutation", () =>
        {
            var f = Fixture(); File.WriteAllText(Path.Combine(f.Paths.Data, "keep"), "keep");
            Reject(() => new ManagerUninstall(f.Paths, f.Addins, f.Config, () => throw new InvalidOperationException("host running"), () => { }).Run());
            Require(File.Exists(Path.Combine(f.Paths.Data, "keep")), "Running host did not block");
        });
        test("UninstallRemovesOnlyExactNativeHostRegistration", () =>
        {
            var f = Fixture(); var year = Path.Combine(f.Addins, "2026"); Directory.CreateDirectory(year);
            var file = Path.Combine(year, "RoBIM.NativeHost.addin");
            var assembly = Path.Combine(f.Paths.Target(Catalog.RevitHost), "Contents", "2026", "RoBIM.Revit.Host.dll");
            File.WriteAllText(file, $"<RevitAddIns><AddIn><Assembly>{System.Security.SecurityElement.Escape(assembly)}</Assembly><AddInId>9DC5FBA5-A8BA-4C29-BD61-A8F80565033F</AddInId><FullClassName>RoBIM.Revit.Host.App</FullClassName></AddIn></RevitAddIns>");
            File.WriteAllText(Path.Combine(year, "Other.addin"), "other");
            new ManagerUninstall(f.Paths, f.Addins, f.Config, () => { }, () => { }).Run();
            Require(!File.Exists(file) && File.Exists(Path.Combine(year, "Other.addin")), "Host registration cleanup wrong");
        });
        test("UninstallForeignHostRegistrationBlocksBeforeMutation", () =>
        {
            var f = Fixture(); var year = Path.Combine(f.Addins, "2026"); Directory.CreateDirectory(year);
            var file = Path.Combine(year, "RoBIM.NativeHost.addin"); File.WriteAllText(file, "<RevitAddIns><AddIn><Assembly>C:\\Other.dll</Assembly></AddIn></RevitAddIns>");
            Reject(() => new ManagerUninstall(f.Paths, f.Addins, f.Config, () => { }, () => { }).Run());
            Require(Directory.Exists(f.Paths.Data) && File.Exists(file), "Foreign registration not preserved");
        });
        test("UninstallWithoutDataIsIdempotentAndClearsManagerCredentials", () =>
        {
            var f = Fixture(); var count = 0;
            var cleanup = new ManagerUninstall(f.Paths, f.Addins, f.Config, () => { }, () => count++);
            cleanup.Run(); cleanup.Run(); Require(count == 2 && !Directory.Exists(f.Paths.Data), "Cleanup cannot repeat");
        });
    }
}
