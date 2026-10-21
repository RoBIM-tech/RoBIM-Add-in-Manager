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
            require(new ExtensionCatalog(paths).Registered().Any(x => x.Repository == "gagaggan/RoBIM-Revit-Addin"), "New user cannot see centrally registered repository");
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
