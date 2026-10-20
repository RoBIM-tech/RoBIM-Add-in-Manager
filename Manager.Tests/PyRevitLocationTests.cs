using System.Text;
using System.Text.Json;
using RoBIM.Manager.Core;

internal static class PyRevitLocationTests
{
    internal static void Run(Action<string, Action> test, Func<string, ManagerPaths> paths, Action<bool, string> require, string root)
    {
        void Reject(Action action) { var rejected=false; try { action(); } catch { rejected=true; } require(rejected,"Expected migration rejection"); }
        string Source(string name, string value)
        {
            var source=Path.Combine(root,"location-sources",name); Directory.CreateDirectory(Path.Combine(source,"lib"));
            Directory.CreateDirectory(Path.Combine(source,"RoBIM 기본.tab","Elements.panel","Example.pushbutton"));
            File.WriteAllText(Path.Combine(source,"lib","shared.py"),value);
            File.WriteAllText(Path.Combine(source,"RoBIM 기본.tab","Elements.panel","Example.pushbutton","script.py"),"# source only"); return source;
        }
        string Ini(ManagerPaths p,string content="[core]\r\nuserextensions = []\r\n[other]\r\nkeep = preserved\r\n") {var file=Path.Combine(p.Data,"pyrevit-test.ini");Directory.CreateDirectory(p.Data);File.WriteAllText(file,content,new UTF8Encoding(true));return file;}
        test("PyRevitLocationMovesOwnedProfilesAndRetainsBackupRestore",()=>
        {
            var p=paths("location-move"); var service=new InstallService(p);
            var first=Source("move-v1","one"); var second=Source("move-v2","two");
            service.Install(Catalog.Revit,new(first,"1")); service.Install(Catalog.Revit,new(second,"2"));
            service.Install(Catalog.ReloadBridge,new(first,"bridge"));
            var old=p.Target(Catalog.Revit); var bridge=p.Target(Catalog.ReloadBridge);
            var foreign=Path.Combine(p.RevitExtensions,"Foreign.extension");Directory.CreateDirectory(foreign);File.WriteAllText(Path.Combine(foreign,"personal.txt"),"keep");
            var ini=Ini(p); var target=Path.Combine(root,"location-targets","공백 있는 새 폴더");
            var result=new PyRevitLocation(p,()=>{},ini).Change(target);
            require(result.MovedExtensions==1 && !Directory.Exists(old) && File.ReadAllText(Path.Combine(p.Target(Catalog.Revit),"lib","shared.py"))=="two","Owned files not relocated");
            require(Directory.Exists(bridge)&&Directory.Exists(foreign)&&File.ReadAllText(ini).Contains("keep = preserved"),"Bridge, foreign extension or unrelated INI changed");
            require(EnvironmentProbe.ConfiguredRevitRoots(ini).Contains(target)&&service.Status(Catalog.Revit) is {Managed:true,Modified:false},"Registration or ownership lost");
            var backup=service.Backups(Catalog.Revit).Single();service.Restore(Catalog.Revit,backup);
            require(File.ReadAllText(Path.Combine(p.Target(Catalog.Revit),"lib","shared.py"))=="one"&&PyRevitLocation.Root(p)==target,"Backup restore did not follow new location");
        });
        test("PyRevitLocationRejectsCollisionAndLocalChanges",()=>
        {
            var p=paths("location-conflict");var service=new InstallService(p);service.Install(Catalog.Revit,new(Source("conflict","old"),"1"));var ini=Ini(p);
            var target=Path.Combine(root,"location-targets","conflict");var existing=Path.Combine(target,Catalog.Revit.FolderName);Directory.CreateDirectory(existing);File.WriteAllText(Path.Combine(existing,"personal.txt"),"keep");
            Reject(()=>new PyRevitLocation(p,()=>{},ini).Change(target));require(File.ReadAllText(Path.Combine(existing,"personal.txt"))=="keep"&&!service.Status(Catalog.Revit).Modified,"Collision overwrote data");
            File.WriteAllText(Path.Combine(p.Target(Catalog.Revit),"lib","personal.py"),"keep");Reject(()=>new PyRevitLocation(p,()=>{},ini).Change(target+"-other"));
            require(File.Exists(Path.Combine(p.Target(Catalog.Revit),"lib","personal.py")),"Dirty source lost");
        });
        test("PyRevitSearchPathPreservesBomOtherRootsAndRejectsAmbiguity",()=>
        {
            var p=paths("location-ini");var ini=Ini(p,"[core]\r\nuserextensions = [\"C:\\\\Existing\"]\r\n[other]\r\nkeep = preserved\r\n");var target=Path.Combine(root,"location-targets","한글 경로");Directory.CreateDirectory(target);
            PyRevitLocation.RegisterSearchRoot(ini,target);var bytes=File.ReadAllBytes(ini);var text=File.ReadAllText(ini);
            require(bytes.Take(3).SequenceEqual(new byte[]{239,187,191})&&text.Contains("C:\\\\Existing")&&text.Contains("keep = preserved"),"INI fields or encoding lost");
            PyRevitLocation.RegisterSearchRoot(ini,target);require(EnvironmentProbe.ConfiguredRevitRoots(ini).Count(x=>x==target)==1,"Duplicate registration");
            File.WriteAllText(ini,"[core]\nuserextensions = []\nuserextensions = []\n");var before=File.ReadAllBytes(ini);Reject(()=>PyRevitLocation.RegisterSearchRoot(ini,target));require(before.SequenceEqual(File.ReadAllBytes(ini)),"Ambiguous INI overwritten");
        });
        test("PyRevitLocationRegistrationFailureRestoresOriginalFiles",()=>
        {
            var p=paths("location-rollback");var service=new InstallService(p);service.Install(Catalog.Revit,new(Source("rollback","old"),"1"));var old=p.Target(Catalog.Revit);
            var ini=Ini(p,"[core]\nuserextensions = not-json\n");var iniBytes=File.ReadAllBytes(ini);var target=Path.Combine(root,"location-targets","rollback");
            Reject(()=>new PyRevitLocation(p,()=>{},ini).Change(target));
            require(Directory.Exists(old)&&!Directory.Exists(Path.Combine(target,Catalog.Revit.FolderName))&&!service.Status(Catalog.Revit).Modified&&iniBytes.SequenceEqual(File.ReadAllBytes(ini)),"Migration rollback failed");
        });
        test("PyRevitLocationRechecksHostBeforeMovingFiles",()=>
        {
            var p=paths("location-host");var service=new InstallService(p);service.Install(Catalog.Revit,new(Source("host","old"),"1"));var ini=Ini(p);var checks=0;
            Reject(()=>new PyRevitLocation(p,()=>{if(++checks>=2)throw new IOException("Host started");},ini).Change(Path.Combine(root,"location-targets","host")));
            require(checks==2&&!service.Status(Catalog.Revit).Modified,"Host recheck changed original installation");
        });
        test("PyRevitLocationRemapsPreparedSourceWithoutLosingSelection",()=>
        {
            var p=paths("location-pending");var service=new InstallService(p);service.Install(Catalog.Revit,new(Source("pending","old"),"1"));var ini=Ini(p);
            var source=p.Target(Catalog.Revit);var id=RevitButtons.Read(source).Single().Id;var reload=new RevitReload(p);reload.PrepareSelection(new(source,"1"),id,true,[]);
            var target=Path.Combine(root,"location-targets","pending");new PyRevitLocation(p,()=>{},ini).Change(target);
            var record=JsonSerializer.Deserialize<ReloadPackage>(File.ReadAllText(Path.Combine(p.Data,"reload-pending.json")))!;
            require(record.SelectionSource==p.Target(Catalog.Revit)&&reload.PendingSelection() is not null,"Prepared source pointer not remapped");
            var pending=reload.PendingSelection()!;new RevitButtonInstaller(p,()=>{}).Set(pending,id,true,[id],()=>Task.CompletedTask).GetAwaiter().GetResult();
            require(RevitButtons.ReadSelection(p.Target(Catalog.RevitSelected))!.Selected.SequenceEqual([id])&&reload.PendingSelection() is null,"Prepared selection could not apply after relocation");
        });
    }
}