using RoBIM.Manager.Core;
internal static class PyRevitRetirementTests
{
    public static void Run(string root, Action<string, Action> test)
    {
        void Require(bool condition) { if (!condition) throw new Exception("Retirement invariant failed"); }
        ManagerPaths Paths(string name) => new(Path.Combine(root,name,"data"), Path.Combine(root,name,"cad"), Path.Combine(root,name,"extensions"));
        void ExistingLegacyInstall(ManagerPaths paths, string source)
        {
            // Seed an installation made by an old Manager. Current install APIs
            // must reject this package, while removal of existing owned data remains supported.
            var target = paths.Target(Catalog.ReloadBridge);
            SafeFiles.Copy(source, target);
            var state = new Dictionary<string, InstallRecord> { [Catalog.ReloadBridge.Id] =
                new(Catalog.ReloadBridge.Id, "41", target, DateTimeOffset.UtcNow, SafeFiles.Hashes(target)) };
            File.WriteAllText(Path.Combine(paths.Data, "installed.json"), System.Text.Json.JsonSerializer.Serialize(state));
        }
        string Source(string name)
        {
            var folder=Path.Combine(root,name,"source");Directory.CreateDirectory(Path.Combine(folder,"lib"));Directory.CreateDirectory(Path.Combine(folder,"RoBIM 관리.tab"));
            File.WriteAllText(Path.Combine(folder,"lib","tool.py"),"old tool");return folder;
        }
        test("RetireConfigRemovesOnlyRoBIMSectionsAndDirectRoots",()=>{
            var before="[core]\r\nuserextensions = [\"C:/Shared/Other.extension\",\"C:/Legacy/RoBIM-dev.extension\"]\r\nsetting = 123\r\n[RoBIM-sync.extension]\r\ndisabled = false\r\n[Other.extension]\r\ndisabled = false\r\npassword = fake-other-value\r\n[RoBIM]\r\ndisabled = false\r\n";
            var cleaned=PyRevitRetirement.RemoveRegistrations(before);
            Require(!cleaned.Contains("[RoBIM") && !cleaned.Contains("RoBIM-dev.extension") && cleaned.Contains("C:/Shared/Other.extension") && cleaned.Contains("setting = 123\r\n") && cleaned.Contains("[Other.extension]\r\ndisabled = false\r\npassword = fake-other-value\r\n"));
            Require(PyRevitRetirement.RemoveRegistrations(cleaned)==cleaned);
        });
        test("RetireBackupHashesAndNativeOtherExtensionsRemain",()=>{
            var paths=Paths("retire-basic");Directory.CreateDirectory(paths.Data);var installer=new InstallService(paths,_=>{});
            ExistingLegacyInstall(paths,Source("retire-basic-src"));
            var foreign=Path.Combine(paths.RevitExtensions,"Other.extension");Directory.CreateDirectory(foreign);File.WriteAllText(Path.Combine(foreign,"script.py"),"foreign");
            var native=Path.Combine(paths.Data,"NativeRevit");Directory.CreateDirectory(native);File.WriteAllText(Path.Combine(native,"active.json"),"preserve native");
            var ini=Path.Combine(root,"retire-basic.ini");File.WriteAllText(ini,"[core]\nuserextensions = []\n[RoBIM]\ndisabled = false\n[Other.extension]\ndisabled = false\n");
            var retirement=new PyRevitRetirement(paths,ini);var plan=retirement.Prepare();Require(Directory.Exists(paths.Target(Catalog.ReloadBridge)));
            retirement.Apply(plan,()=>{},()=>true);
            Require(!Directory.Exists(paths.Target(Catalog.ReloadBridge)) && RevitRuntimePolicy.NativeOnly(paths));
            Require(File.ReadAllText(Path.Combine(foreign,"script.py"))=="foreign" && File.ReadAllText(Path.Combine(native,"active.json"))=="preserve native" && !File.ReadAllText(ini).Contains("[RoBIM]"));
            Require(File.Exists(Path.Combine(paths.Data,"Backups","PyRevitRetirement",plan.Id,Catalog.ReloadBridge.Id,"lib","tool.py")));
            try { installer.Install(Catalog.ReloadBridge,new(Source("retire-blocked"),"42")); } catch(IOException) { return; }
            throw new Exception("Retired bridge reinstalled");
        });
        test("RetireRejectsConfigChangesBeforeDeletingFiles",()=>{
            var paths=Paths("retire-concurrent");Directory.CreateDirectory(paths.Data);ExistingLegacyInstall(paths,Source("retire-concurrent-src"));
            var ini=Path.Combine(root,"retire-concurrent.ini");File.WriteAllText(ini,"[core]\nuserextensions = []\n");var retirement=new PyRevitRetirement(paths,ini);var plan=retirement.Prepare();File.AppendAllText(ini,"personal change\n");
            try { retirement.Apply(plan,()=>{},()=>true); } catch(IOException) { Require(Directory.Exists(paths.Target(Catalog.ReloadBridge)) && File.ReadAllText(ini).Contains("personal change"));return; }
            throw new Exception("Concurrent config overwritten");
        });
        test("RetireRequiresNativeHostBeforeDeletingFiles",()=>{
            var paths=Paths("retire-host");Directory.CreateDirectory(paths.Data);ExistingLegacyInstall(paths,Source("retire-host-src"));
            var retirement=new PyRevitRetirement(paths,Path.Combine(root,"no-ini"));var plan=retirement.Prepare();
            try { retirement.Apply(plan,()=>{},()=>false); } catch(IOException) { Require(Directory.Exists(paths.Target(Catalog.ReloadBridge)));return; }
            throw new Exception("Host prerequisite bypassed");
        });
        test("NativeOnlyRejectsOtherPyRevitPackagesWithoutTouchingThem",()=>{
            var paths=Paths("retire-other-allowed");Directory.CreateDirectory(paths.Data);File.WriteAllText(Path.Combine(paths.Data,"revit-mode.json"),"{\"Mode\":\"NativeOnly\"}");
            var foreign = Path.Combine(paths.RevitExtensions,"Other.extension");Directory.CreateDirectory(foreign);File.WriteAllText(Path.Combine(foreign,"script.py"),"preserve");
            try { RevitRuntimePolicy.EnsureAllowed(paths,new("other","Other",ToolKind.PyRevit,"other/project","main","Other.extension","foreign",true)); }
            catch(IOException) { Require(File.ReadAllText(Path.Combine(foreign,"script.py"))=="preserve");return; }
            throw new Exception("Retired pyRevit installation allowed");
        });
    }
}
