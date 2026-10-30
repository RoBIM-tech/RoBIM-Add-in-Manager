using RoBIM.Manager.Core;
using RoBIM.Cad.Configuration;
using System.Text.Json;

internal static class CadBundleMigrationTests
{
    public static void Run(string root, Action<string, Action> test)
    {
        void Require(bool ok,string reason){if(!ok)throw new Exception(reason);}
        void Reject(Action action){try{action();}catch(Exception){return;}throw new Exception("Unprotected migration");}
        (ManagerPaths Paths, ToolSpec Legacy, string Source) Fixture(string name)
        {
            var directory=Path.Combine(root,"bundle-migration",name);var paths=new ManagerPaths(Path.Combine(directory,"data"),Path.Combine(directory,"plugins"),Path.Combine(directory,"revit"));
            var source=Path.Combine(directory,"source");var windows=CadReload.WindowsDirectory(source);Directory.CreateDirectory(windows);
            File.WriteAllText(Path.Combine(windows,"RoBIM.Cad.Loader.dll"),"old loader");
            File.WriteAllText(Path.Combine(source,"PackageContents.xml"),"<ApplicationPackage><Components><ComponentEntry ModuleName=\"./Contents/Windows/RoBIM.Cad.Loader.dll\"/></Components></ApplicationPackage>");
            CadFiles.Save(Path.Combine(windows,CadFiles.ManifestName),new CadCatalog());
            var legacy=Catalog.Cad with{FolderName=CadLoaderAssets.LegacyBundleName};
            new InstallService(paths).Install(legacy,new(source,"1.0.2-test.2"));
            return(paths,legacy,source);
        }
        test("LegacyCadInstallIdNormalizesWithoutWritingAndRejectsConflict",()=>{
            var f=Fixture("identity"); var installer=new InstallService(f.Paths);
            var file=Path.Combine(f.Paths.Data,"installed.json");
            var original=installer.ReadState()[Catalog.Cad.Id];
            File.WriteAllText(file,JsonSerializer.Serialize(new Dictionary<string,InstallRecord>{{"delhidden",original with{Id="delhidden"}}}));
            var before=File.ReadAllText(file);
            Require(installer.ReadState()[Catalog.Cad.Id].Id==Catalog.Cad.Id,"Legacy record not normalized");
            Require(File.ReadAllText(file)==before,"Read rewrote state");
            File.WriteAllText(file,JsonSerializer.Serialize(new Dictionary<string,InstallRecord>{{"delhidden",original with{Id="delhidden"}},{Catalog.Cad.Id,original with{Version="conflict"}}}));
            Reject(()=>installer.ReadState());
        });
        test("CadBundleLegacyRegistryNormalizesWithoutRewritingUserFile",()=>{
            var directory=Path.Combine(root,"legacy-loader-registry");Directory.CreateDirectory(directory);
            var paths=new ManagerPaths(directory,Path.Combine(directory,"plugins"),Path.Combine(directory,"revit"));
            var old=new ExtensionPackage("delhidden","CAD","autocad",CadLoaderAssets.LegacyBundleName,CadLoaderAssets.LegacyBundleName,"release",CadLoaderAssets.LegacyName);
            var repo=new ExtensionRepository("gagaggan/RoBIM-CAD","main",[old],"fixture");
            var file=Path.Combine(directory,"extension-repositories.json");File.WriteAllText(file,JsonSerializer.Serialize(new[]{repo}));var before=File.ReadAllBytes(file);
            var result=new ExtensionCatalog(paths).Registered().Single(r=>r.Repository==Catalog.Cad.Repository);
            var normalized=result.Packages.Single(p=>p.Id==Catalog.Cad.Id);
            Require(normalized.Folder==CadLoaderAssets.BundleName&&normalized.Asset==CadLoaderAssets.Name,"Legacy registry not normalized");
            Require(ExtensionCatalog.CurrentPackage(old,result)==normalized&&ExtensionCatalog.Tool(repo,old).Id==Catalog.Cad.Id,"Old package identity not preserved");
            Require(before.SequenceEqual(File.ReadAllBytes(file)),"Reading registry rewrote user data");
        });
        test("CadBundleRenamePreservesIdentityBackupAndRibbon",()=>{
            var f=Fixture("success");var old=f.Paths.Target(f.Legacy);var service=new InstallService(f.Paths);
            var ribbon=Path.Combine(f.Paths.Data,"ribbon-layout-autocad.json");File.WriteAllText(ribbon,"user layout bytes");
            Require(service.Status(Catalog.Cad).Managed && f.Paths.Target(Catalog.Cad)==old,"Legacy install was lost before update");
            service.Install(Catalog.Cad,new(f.Source,"1.1.0-test.3"));
            Require(!Directory.Exists(old) && Directory.Exists(f.Paths.CanonicalTarget(Catalog.Cad)),"Old and new bundle left active");
            Require(service.ReadState()[Catalog.Cad.Id].Path==f.Paths.CanonicalTarget(Catalog.Cad),"Record identity/path changed incorrectly");
            Require(File.ReadAllText(ribbon)=="user layout bytes" && service.Backups(Catalog.Cad).Count==1,"Backup or ribbon lost");
            service.Restore(Catalog.Cad,service.Backups(Catalog.Cad).Single());
            Require(service.Status(Catalog.Cad).Version=="1.0.2-test.2" && !Directory.Exists(old),"Historical backup cannot restore into renamed bundle");
        });
        test("CadBundleRenameRejectsModifiedAndExternalLegacy",()=>{
            var f=Fixture("modified");var old=f.Paths.Target(f.Legacy);File.AppendAllText(Path.Combine(CadReload.WindowsDirectory(old),"RoBIM.Cad.Loader.dll"),"user change");
            Reject(()=>new InstallService(f.Paths).Install(Catalog.Cad,new(f.Source,"1.1.0-test.3")));
            Require(Directory.Exists(old)&&!Directory.Exists(f.Paths.CanonicalTarget(Catalog.Cad)),"Modified old install moved");
            var g=Fixture("external");File.Delete(Path.Combine(g.Paths.Data,"installed.json"));
            Reject(()=>new InstallService(g.Paths).Install(Catalog.Cad,new(g.Source,"1.1.0-test.3")));
            Require(Directory.Exists(g.Paths.Target(g.Legacy)),"External install moved");
        });
        test("CadBundleRenameRejectsConflictingDestination",()=>{
            var f=Fixture("conflict");var target=f.Paths.CanonicalTarget(Catalog.Cad);Directory.CreateDirectory(target);File.WriteAllText(Path.Combine(target,"external.keep"),"keep");
            Reject(()=>new InstallService(f.Paths).Install(Catalog.Cad,new(f.Source,"1.1.0-test.3")));
            Require(File.ReadAllText(Path.Combine(target,"external.keep"))=="keep" && Directory.Exists(f.Paths.Target(f.Legacy)),"Conflict overwritten");
        });
        test("CadBundleRenameRollsBackBothPathAndRecordOnSaveFailure",()=>{
            var f=Fixture("rollback");var old=f.Paths.Target(f.Legacy);var state=Path.Combine(f.Paths.Data,"installed.json");var before=File.ReadAllBytes(state);
            using(var locked=new FileStream(state,FileMode.Open,FileAccess.Read,FileShare.Read))
                Reject(()=>new InstallService(f.Paths).Install(Catalog.Cad,new(f.Source,"1.1.0-test.3")));
            Require(Directory.Exists(old)&&!Directory.Exists(f.Paths.CanonicalTarget(Catalog.Cad))&&before.SequenceEqual(File.ReadAllBytes(state)),"Save failure did not restore old path/record");
        });
        test("CadBundleRenameRebasesExistingPreparedSelection",()=>{
            var f=Fixture("selection");var old=f.Paths.Target(f.Legacy);var source=Path.Combine(root,"migration-feature");Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source,"feature.dll"),"feature");
            var module=new CadModule{Id="feature",Name="Feature",Assembly="feature.dll",AssemblyName="Feature",EntryType="Feature.Entry",Actions=[new(){Id="run",Title="Run",ExecuteKey="RUN"}]};
            var windows=CadReload.WindowsDirectory(f.Source);File.Copy(Path.Combine(source,"feature.dll"),Path.Combine(windows,"feature.dll"));
            var catalog=new CadCatalog{Modules=[module]};CadFiles.Save(Path.Combine(windows,CadFiles.ManifestName),catalog);
            // Update the legacy fixture through the normal owned installer before preparing.
            new InstallService(f.Paths).Install(f.Legacy,new(f.Source,"1.0.2-test.2"));
            var bridge=new CadReload(f.Paths);var request=bridge.Prepare(f.Legacy,f.Source,catalog,["feature:run"],null);
            var previous=bridge.Source(old,preferInstalled:true)!;
            new InstallService(f.Paths).Install(Catalog.Cad,new(f.Source,"1.1.0-test.3"));
            var rebased=new CadFeaturePackages(f.Paths).Rebase(previous,request.SelectedActions);
            Require(rebased.BundlePath==f.Paths.CanonicalTarget(Catalog.Cad)&&rebased.SelectedActions.SequenceEqual(request.SelectedActions),"Selection/path lost during migration");
            _=CadFiles.Snapshot(bridge.Data,rebased);_=CadFiles.Snapshot(bridge.Data,request);
        });
    }
}
