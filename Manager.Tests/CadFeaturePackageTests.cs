using RoBIM.Cad.Configuration;
using RoBIM.Manager.Core;
using System.Text.Json;

internal static class CadFeaturePackageTests
{
    public static void Run(string root, Action<string, Action> test)
    {
        void Require(bool b, string reason) { if (!b) throw new Exception(reason); }
        void Reject(Action action) { try { action(); } catch (IOException) { return; } throw new Exception("Accepted invalid package"); }
        var dir = Path.Combine(root, "independent-cad");
        var paths = new ManagerPaths(Path.Combine(dir,"data"),Path.Combine(dir,"plugins"),Path.Combine(dir,"revit"));
        var loader = Path.Combine(dir,"loader");var windows = CadReload.WindowsDirectory(loader); Directory.CreateDirectory(windows);
        File.WriteAllText(Path.Combine(windows,"RoBIM.Cad.Loader.dll"),"loader");
        File.WriteAllText(Path.Combine(loader,"PackageContents.xml"),"<ApplicationPackage><Components><ComponentEntry ModuleName=\"./Contents/Windows/RoBIM.Cad.Loader.dll\"/></Components></ApplicationPackage>");
        CadFiles.Save(Path.Combine(windows,CadFiles.ManifestName),new CadCatalog());
        new InstallService(paths).Install(Catalog.Cad,new(loader,"1.1.0-test.1"));
        var baseline = SafeFiles.Hashes(paths.Target(Catalog.Cad));
        var packages = new CadFeaturePackages(paths);
        ExtensionPackage Package(string id) => new("cad-"+id,id,"autocad","Feature.bundle","Feature.bundle","release","feature.zip",Channel:"test",ModuleId:id,CommandId:"run");
        SourcePackage Source(string id,string version="1.1.0-test.1")
        {
            var bundle=Path.Combine(dir,id+version);var payload=CadReload.WindowsDirectory(bundle);Directory.CreateDirectory(payload);
            File.WriteAllText(Path.Combine(payload,id+".dll"),id+version);
            File.WriteAllText(Path.Combine(payload,CadFiles.ManifestName),JsonSerializer.Serialize(new {schemaVersion=1,modules=new[]{new {id,name=id,version,channel="test",assembly=id+".dll",assemblyName=id,entryType=id+".Entry",actions=new[]{new{id="run",title=id,executeKey="RUN",panel="Tools"}}}}}));
            return new(bundle,version);
        }
        var first=Source("first");var second=Source("second");
        test("IndependentCadPackagesPreserveOtherSelectionsAndLoader",()=>
        {
            packages.Set(Package("first"),first,true,null);
            var request=packages.Set(Package("second"),second,true,null);
            Require(request.SelectedActions.Order().SequenceEqual(new[]{"first:run","second:run"}),"Other selection lost");
            Require(SafeFiles.Same(baseline,SafeFiles.Hashes(paths.Target(Catalog.Cad))),"Loader installation changed");
        });
        test("IndependentCadUpdateChangesOnlyChosenModule",()=>
        {
            var request=packages.Set(Package("first"),Source("first","1.2.0-test.1"),true,null);
            var catalog=CadFiles.Catalog(CadFiles.Snapshot(Path.Combine(paths.Data,"Cad"),request));
            Require(catalog.Modules.Single(m=>m.Id=="second").Version=="1.1.0-test.1","Other module updated");
            Require(request.SelectedActions.Count==2,"Selections lost");
        });
        test("IndependentCadRemovalDoesNotNeedRemotePackage",()=>
        {
            var request=packages.Set(Package("first"),null,false,null);
            Require(request.SelectedActions.SequenceEqual(new[]{"second:run"}),"Removal affected another button");
        });
        test("IndependentCadRejectsWrongIdChannelAndBundledLoader",()=>
        {
            Reject(()=>CadFeaturePackages.Validate(first.Path,Package("wrong")));
            Reject(()=>CadFeaturePackages.Validate(first.Path,Package("first") with {Channel="stable"}));
            File.WriteAllText(Path.Combine(CadReload.WindowsDirectory(first.Path),"RoBIM.Cad.Loader.dll"),"unexpected");
            Reject(()=>CadFeaturePackages.Validate(first.Path,Package("first")));
        });
        test("IndependentCadRenamePreservesLegacyInstallationId",()=>
        {
            Require(RepositoryOwnership.Canonical("gagaggan/RoBIM-CAD")==Catalog.Cad.Repository,"Old owner not mapped");
            Require(RepositoryOwnership.Canonical("RoBIM-tech/RoBIM-CAD")==Catalog.Cad.Repository,"Old name not mapped");
            Require(RepositoryOwnership.LegacyIdentity(Catalog.Cad.Repository)=="gagaggan/RoBIM-CAD","Legacy key changed");
        });
        test("IndependentCadReleaseOverrideKeepsIdentity",()=>
        {
            var original=Package("first");var stable=ExtensionCatalog.ReleaseChannel(original,"stable");
            Require(stable.ModuleId==original.ModuleId && original.Channel=="test","Channel override changed registration");
        });
        test("TeamConnectionFailureIsRetryableWithoutGrantingAccess",()=>
        {
            using var access=new TeamAccess(false,()=>Task.FromResult(new GitHubIdentity("alice","Device")),_=>throw new IOException("offline"));
            var result=access.Refresh().GetAwaiter().GetResult();
            Require(!access.Allowed && result.State==AccountAccessState.RetryPending && result.Login=="alice","Transient error became signed out");
        });
        test("InvalidSavedAuthenticationRequiresExplicitReauthentication",()=>
        {
            using var access=new TeamAccess(false,()=>throw new GitHubAuthenticationException("revoked"));
            var result=access.Refresh().GetAwaiter().GetResult();
            Require(!access.Allowed && result.State==AccountAccessState.ReauthenticationRequired,"Invalid credential considered transient");
        });
    }
}
