using System.IO.Compression;
using RoBIM.Manager.Core;

internal static class ReloadBridgeTests
{
    internal static void Run(string root, Action<string, Action> test)
    {
        void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
        string Source(string name, string script, string? binary = null)
        {
            var folder = Path.Combine(root, name, Catalog.ReloadBridge.FolderName);
            Directory.CreateDirectory(Path.Combine(folder, "lib"));
            Directory.CreateDirectory(Path.Combine(folder, "RoBIM 관리.tab/Manager.panel/Reload.pushbutton"));
            File.WriteAllText(Path.Combine(folder, "lib/robim_ribbon.py"), script);
            if (binary is not null) File.WriteAllText(Path.Combine(folder, "lib/runtime.dll"), binary);
            return folder;
        }
        string Archive(string folder)
        {
            var zip = Path.GetDirectoryName(folder)! + ".zip";
            ZipFile.CreateFromDirectory(Path.GetDirectoryName(folder)!, zip);
            File.WriteAllText(zip + ".sha256", SafeFiles.Hash(zip)); return zip;
        }
        ManagerPaths Paths(string name) => new(Path.Combine(root,name,"data"),Path.Combine(root,name,"cad"),Path.Combine(root,name,"extensions"));
        InstallService Installed(ManagerPaths paths, string? binary = null)
        {
            var service = new InstallService(paths, _ => { });
            service.Install(Catalog.ReloadBridge, new(Source(Guid.NewGuid().ToString("N"),"old",binary),"37")); return service;
        }
        void Reject(Action action) { try { action(); } catch (Exception ex) when (ex is IOException or InvalidOperationException) { return; } throw new Exception("Expected rejection"); }
        var session = new RevitSession(1234,5678);
        test("BridgeScriptHotUpdateBacksUpAndRequestsStandardReload", () =>
        {
            var paths=Paths("bridge-hot");var service=Installed(paths);var checks=0;var requests=0;
            ReloadBridge.Install(paths,Archive(Source("bridge-hot-new","new")),()=>session,_=>checks++,()=>requests++);
            Require(checks>=3 && requests==1,"Session checks or standard reload request missing");
            Require(service.Status(Catalog.ReloadBridge) is {Managed:true,Modified:false,Version:"1.0.0-preview.38"},"Updated bridge ownership mismatch");
            Require(service.Backups(Catalog.ReloadBridge).Count==1 && File.ReadAllText(Path.Combine(paths.Target(Catalog.ReloadBridge),"lib/robim_ribbon.py"))=="new","Backup/delta missing");
        });
        test("BridgeHotUpdateRejectsPreparedChoicesWithoutConsumingThem", () =>
        {
            var paths=Paths("bridge-pending");var service=Installed(paths);var before=SafeFiles.Hashes(paths.Target(Catalog.ReloadBridge));
            var pending=Path.Combine(paths.Data,"reload-pending.json");File.WriteAllText(pending,"user choice");
            Reject(()=>ReloadBridge.Install(paths,Archive(Source("bridge-pending-new","new")),()=>session,_=>{},()=>throw new Exception("Unexpected request")));
            Require(SafeFiles.Same(before,SafeFiles.Hashes(paths.Target(Catalog.ReloadBridge))) && File.ReadAllText(pending)=="user choice","Pending selection or bridge changed");
        });
        test("BridgeHotUpdateRejectsModifiedFilesAndSessionChanges", () =>
        {
            var paths=Paths("bridge-modified");var service=Installed(paths);var file=Path.Combine(paths.Target(Catalog.ReloadBridge),"lib/robim_ribbon.py");File.WriteAllText(file,"user edit");
            var zip=Archive(Source("bridge-modified-new","new"));
            Reject(()=>ReloadBridge.Install(paths,zip,()=>session,_=>{},()=>{}));
            Require(File.ReadAllText(file)=="user edit","User edit overwritten");
            var clean=Paths("bridge-session");Installed(clean);var before=SafeFiles.Hashes(clean.Target(Catalog.ReloadBridge));
            Reject(()=>ReloadBridge.Install(clean,zip,()=>session,_=>throw new IOException("Session changed"),()=>{}));
            Require(SafeFiles.Same(before,SafeFiles.Hashes(clean.Target(Catalog.ReloadBridge))),"Changed session mutated live files");
        });
        test("BridgeHotUpdateRejectsBinaryChanges", () =>
        {
            var paths=Paths("bridge-binary");Installed(paths,"old binary");var before=SafeFiles.Hashes(paths.Target(Catalog.ReloadBridge));
            Reject(()=>ReloadBridge.Install(paths,Archive(Source("bridge-binary-new","new","new binary")),()=>session,_=>{},()=>{}));
            Require(SafeFiles.Same(before,SafeFiles.Hashes(paths.Target(Catalog.ReloadBridge))),"Live binary changed");
        });
        test("BridgeRequestFailureLeavesVerifiedFilesPreparedForManualReload", () =>
        {
            var paths=Paths("bridge-request");var service=Installed(paths);
            Reject(()=>ReloadBridge.Install(paths,Archive(Source("bridge-request-new","new")),()=>session,_=>{},()=>throw new IOException("No bridge session")));
            Require(service.Status(Catalog.ReloadBridge) is {Managed:true,Modified:false,Version:"1.0.0-preview.38"},"Prepared files not tracked after request failure");
        });
    }
}
