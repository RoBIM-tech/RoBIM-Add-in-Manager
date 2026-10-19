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
