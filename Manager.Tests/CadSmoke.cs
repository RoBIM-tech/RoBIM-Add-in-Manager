using System.Text.Json;
using RoBIM.Cad.Configuration;
using RoBIM.Manager.Core;

internal static class CadSmoke
{
    public static void Prepare(string directory, string bundle)
    {
        var paths = new ManagerPaths(Path.Combine(directory, "data"), Path.Combine(directory, "plugins"), Path.Combine(directory, "revit"));
        new InstallService(paths).Install(Catalog.Cad, new(bundle, "smoke"));
        var service = new CadReload(paths);
        var catalog = CadFiles.Catalog(CadReload.WindowsDirectory(bundle));
        var request = service.Prepare(Catalog.Cad, bundle, catalog, ["delhidden:preview"], null);
        File.WriteAllText(Path.Combine(directory, "smoke-info.json"), JsonSerializer.Serialize(new
        {
            Data = service.Data, Bundle = paths.Target(Catalog.Cad), Request = request.Id
        }));
    }
}
