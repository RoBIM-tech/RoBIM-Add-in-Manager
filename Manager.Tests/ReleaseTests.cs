using System.Text.Json;
using RoBIM.Manager.Core;
using RoBIM.Common;
using RoBIM.Revit.Reload;
using RoBIM.Cad.Configuration;

internal static class ReleaseTests
{
    public static void Run(Action<string, Action> test, Action<bool,string> require, string root)
    {
        test("CentralCatalogContainsNativeAndCad", () => {
            var c = BundledCatalog.Read();
            require(c.Commands.Any(x => x.Host == "revit") && c.Commands.Any(x => x.Host == "autocad"), "Missing central tools");
            var paths = new ManagerPaths(Path.Combine(root,"central-catalog"), Path.Combine(root,"cad"), Path.Combine(root,"revit"));
            var native = new ExtensionCatalog(paths).Registered().Where(r => r.Packages.Any(p => p.Host == "revit")).ToArray();
            require(native.All(r => r.Packages.Length == 1) && new[] { "RoBIM-Revit-Sheets", "RoBIM-Revit-ParameterCopy", "RoBIM-Revit-ParameterBulkEdit" }.All(name => native.Any(r => r.Repository == "gagaggan/" + name)), "Missing independent button repositories");
            require(Catalog.RevitHost.Repository == "gagaggan/RoBIM-Revit-Addin", "Host was moved into a feature repository");
        });
        test("RepositoryMovePreservesRegistryAndSheetInstallationIdentity", () => {
            var defaults = BundledCatalog.Read();
            var sheets = defaults.Repositories.Single(r => r.Repository == "gagaggan/RoBIM-Revit-Sheets");
            var old = sheets with { Repository = "gagaggan/RoBIM-Revit-Addin" };
            var unknown = new ExtensionPackage("personal", "Personal", "revit", "Personal.bundle", "Personal.bundle", "release", "Personal.zip");
            var prototype = new ExtensionPackage("robim-revit-parameters", "Old prototype", "revit", "RoBIM.Revit.Parameters.bundle", "RoBIM.Revit.Parameters.bundle", "release", "Old.zip");
            var paths = new ManagerPaths(Path.Combine(root,"repository-move"), Path.Combine(root,"cad"), Path.Combine(root,"revit"));
            Directory.CreateDirectory(paths.Data);
            var file = Path.Combine(paths.Data,"extension-repositories.json");
            File.WriteAllText(file, JsonSerializer.Serialize(new[]{old with { Packages = [sheets.Packages[0],prototype,unknown] }}));
            var before = File.ReadAllBytes(file);
            var registered = new ExtensionCatalog(paths).Registered();
            require(registered.Single(r => r.Repository == old.Repository).Packages.Single().Id == "personal", "Unrelated registration removed or old buttons duplicated");
            require(registered.Count(r => r.Repository == sheets.Repository) == 1 && registered.Any(r => r.Repository == "gagaggan/RoBIM-Revit-ParameterCopy") && registered.Any(r => r.Repository == "gagaggan/RoBIM-Revit-ParameterBulkEdit"), "New targets missing");
            require(before.SequenceEqual(File.ReadAllBytes(file)), "Reading registry changed user data");
            require(ExtensionCatalog.Tool(old,sheets.Packages[0]).Id == ExtensionCatalog.Tool(sheets,sheets.Packages[0]).Id, "Sheet installation ID changed on repository move");
            require(!Directory.Exists(Path.Combine(paths.Data,"NativeRevit")), "Registry move installed or selected a feature");
            var normalize = typeof(ExtensionCatalog).GetMethod("CurrentSources",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!;
            var discovered = (ExtensionRepository[])normalize.Invoke(null,new object[]{new[]{old,sheets}})!;
            require(discovered.Length == 1 && discovered[0].Repository == sheets.Repository, "Remote topic discovery reintroduced moved source cards");

        });
        test("NativeMaturityDoesNotGuessStableFromNumericVersion", () => {
            var dir = Path.Combine(root,"maturity"); Directory.CreateDirectory(dir);
            require(ModuleReleases.Native(dir,"sample","1.0.0").Channel == "unknown", "Guessed stable");
            require(ModuleReleases.Native(dir,"sample","1.0.0-preview.1").Channel == "test", "Lost legacy test classification");
            File.WriteAllText(Path.Combine(dir,ReleaseMetadata.FileName), JsonSerializer.Serialize(new ReleaseManifest(1,[new("sample","1.0.0","test")]),ReleaseMetadata.Json));
            require(ModuleReleases.Native(dir,"sample","1.0.0").Channel == "test", "Explicit TEST ignored");
            var failed = false; try { ModuleReleases.Native(dir,"sample","2.0.0"); } catch (InvalidDataException) { failed = true; }
            require(failed,"Accepted mismatched metadata");
        });
        test("CadSnapshotRetainsMaturityMetadata", () => {
            var json = "{\"schemaVersion\":1,\"modules\":[{\"id\":\"sample\",\"version\":\"1.0.0\",\"channel\":\"test\",\"actions\":[]}]}";
            var catalog = JsonSerializer.Deserialize<CadCatalog>(json,CadFiles.Json)!;
            var dir = Path.Combine(root,"cad-release"); Directory.CreateDirectory(dir);
            CadFiles.Save(Path.Combine(dir,CadFiles.ManifestName),catalog);
            require(ModuleReleases.Cad(dir,"sample","1.0.0").Channel == "test","CAD snapshot lost TEST declaration");
        });
    }
}
