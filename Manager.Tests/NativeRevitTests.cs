using RoBIM.Manager.Core;
internal static class NativeRevitTests
{
    public static void Run(Action<string,Action> test,Func<string,ManagerPaths> paths,Action<bool,string> require,string root)
    {
        const string manifest="""{"schemaVersion":1,"packages":[{"id":"sheets","name":"Native sheets","host":"revit","path":"RoBIM.Sheets.bundle","folder":"RoBIM.Sheets.bundle","distribution":"release","asset":"sheets.zip"}]}""";
        var package=ExtensionCatalog.ParseManifest(manifest).Single();
        var repo=new ExtensionRepository("example/native-revit","main",[package],"fixture");
        var tool=ExtensionCatalog.Tool(repo,package);
        string Fixture(string name,string module="Contents/2026/Sheets.addin",string assembly="Sheets.dll",string platform="Revit")
        {
            var directory=Path.Combine(root,"native-"+name);Directory.CreateDirectory(Path.Combine(directory,"Contents","2026"));
            File.WriteAllText(Path.Combine(directory,"PackageContents.xml"),$"<ApplicationPackage><Components><RuntimeRequirements OS='Win64' Platform='{platform}' SeriesMin='R2026' SeriesMax='R2026'/><ComponentEntry ModuleName='{module}'/></Components></ApplicationPackage>");
            File.WriteAllText(Path.Combine(directory,"Contents","2026","Sheets.addin"),$"<RevitAddIns><AddIn Type='Application'><Assembly>{assembly}</Assembly><AddInId>882D0971-3657-4B1E-825D-611225084297</AddInId><FullClassName>Native.App</FullClassName></AddIn></RevitAddIns>");
            File.WriteAllText(Path.Combine(directory,"Contents","2026","Sheets.dll"),"fixture");return directory;
        }
        void Reject(Action action){var rejected=false;try{action();}catch(Exception e) when(e is InvalidDataException or IOException or System.Xml.XmlException){rejected=true;}require(rejected,"Unsafe native package accepted");}
        if (Environment.GetEnvironmentVariable("ROBIM_NATIVE_TEST_PACKAGE") is { } compiled)
            test("NativeRevitCompiledPackageAccepted",()=>InstallService.ValidatePackage(tool,compiled));
        test("NativeRevitCatalogAndTarget",()=>require(tool.Kind==ToolKind.NativeRevit&&paths("native-target").Target(tool)==Path.Combine(paths("native-target").CadPlugins,package.Folder),"Native tool path/type mismatch"));
        test("NativeRevitValidPackage",()=>InstallService.ValidatePackage(tool,Fixture("valid")));
        test("NativeRevitModuleTraversalRejected",()=>Reject(()=>InstallService.ValidatePackage(tool,Fixture("escape","../escape.addin"))));
        test("NativeRevitAssemblyTraversalRejected",()=>Reject(()=>InstallService.ValidatePackage(tool,Fixture("asm-escape",assembly:"../../escape.dll"))));
        test("NativeRevitAbsoluteAssemblyRejected",()=>Reject(()=>InstallService.ValidatePackage(tool,Fixture("asm-absolute",assembly:@"C:\outside.dll"))));
        test("NativeRevitWrongPlatformRejected",()=>Reject(()=>InstallService.ValidatePackage(tool,Fixture("cad",platform:"AutoCAD"))));
        test("NativeRevitSourceOnlyDistributionRejected",()=>Reject(()=>ExtensionCatalog.ParseManifest(manifest.Replace("\"release\"","\"git\""))));
        test("NativeRevitHostRunningBlocked",()=>require(InstallFlow.Decide(new(tool,"target","1",true,false,false,""),true,true,false).Action==InstallAction.WaitForHost,"Native package update allowed while Revit runs"));
        test("NativeVerifiedExternalAdoptionAndBackup",()=>
        {
            var target=paths("native-adopt"); var reference=Fixture("adopt-reference"); SafeFiles.Copy(reference,target.Target(tool));
            var service=new InstallService(target,_=>{}); var before=SafeFiles.Hashes(target.Target(tool));
            require(service.Status(tool).External,"Fixture is not external"); service.AdoptVerifiedNative(tool,new(reference,"known-v1"));
            require(service.Status(tool).Managed&&SafeFiles.Same(before,SafeFiles.Hashes(target.Target(tool))),"Adoption replaced existing files");
            service.Remove(tool); var backup=service.Backups(tool).First();
            require(SafeFiles.Same(before,SafeFiles.Hashes(Path.Combine(backup.Directory,"content"))),"Migration backup differs from original");
        });
        test("NativeExternalAdoptionRejectsLocalChanges",()=>
        {
            var target=paths("native-adopt-modified"); var reference=Fixture("adopt-modified-reference"); SafeFiles.Copy(reference,target.Target(tool));
            File.WriteAllText(Path.Combine(target.Target(tool),"personal.keep"),"preserve"); var service=new InstallService(target,_=>{});
            Reject(()=>service.AdoptVerifiedNative(tool,new(reference,"known-v1")));
            require(service.Status(tool).External&&File.Exists(Path.Combine(target.Target(tool),"personal.keep")),"External local change lost");
        });
        test("NativeExternalAdoptionRechecksHostBeforeRecordingOwnership",()=>
        {
            var target=paths("native-adopt-race"); var reference=Fixture("adopt-race-reference"); SafeFiles.Copy(reference,target.Target(tool)); var calls=0;
            var service=new InstallService(target,_=>{if(++calls==2)throw new IOException("host started");});
            Reject(()=>service.AdoptVerifiedNative(tool,new(reference,"known-v1")));
            require(calls==2&&service.Status(tool).External,"Host race adopted external package");
        });
        test("NativeExternalAdoptionRequiresSeparateNativeReference",()=>
        {
            var target=paths("native-adopt-reference"); var reference=Fixture("adopt-separate-reference"); SafeFiles.Copy(reference,target.Target(tool)); var service=new InstallService(target,_=>{});
            Reject(()=>service.AdoptVerifiedNative(tool,new(target.Target(tool),"self")));
            Reject(()=>service.AdoptVerifiedNative(Catalog.Cad,new(reference,"wrong kind")));
            require(service.Status(tool).External,"Invalid reference adopted package");
        });
        test("NativeRevitInstallUpdateRestore",()=>
        {
            var service=new InstallService(paths("native-install"));var first=Fixture("install-1");var second=Fixture("install-2");
            File.AppendAllText(Path.Combine(second,"Contents","2026","Sheets.dll"),"v2");
            service.Install(tool,new(first,"1"));service.Install(tool,new(second,"2"));
            require(service.Status(tool).Managed&&service.Status(tool).Version=="2","Native install/update failed");
            service.Restore(tool,service.Backups(tool).First());require(service.Status(tool).Version=="1","Native restore failed");
            service.Remove(tool);require(!Directory.Exists(service.Paths.Target(tool)),"Native remove failed");
        });
    }
}
