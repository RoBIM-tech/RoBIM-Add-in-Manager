using System.Text.Json;
using RoBIM.Cad.Configuration;
using RoBIM.Manager.Core;

internal static class CadSplitArchiveSmoke
{
    public static void Run(string directory, string loaderZip, string[] featureZips)
    {
        Directory.CreateDirectory(directory);
        var paths=new ManagerPaths(Path.Combine(directory,"data"),Path.Combine(directory,"plugins"),Path.Combine(directory,"revit"));
        var loader=Path.Combine(directory,"loader");SafeFiles.Extract(loaderZip,loader);
        var installer=new InstallService(paths);installer.Install(Catalog.Cad,new(CadLoaderAssets.ExtractedBundle(loader),System.Diagnostics.FileVersionInfo.GetVersionInfo(Path.Combine(CadLoaderAssets.ExtractedBundle(loader),"Contents","Windows","RoBIM.Cad.Loader.dll")).ProductVersion!.Split('+')[0]));
        var service=new CadFeaturePackages(paths);var selected=new List<string>();CadRequest? request=null;
        var packages=new List<ExtensionPackage>();
        foreach(var zip in featureZips)
        {
            var extracted=Path.Combine(directory,Guid.NewGuid().ToString("N"));SafeFiles.Extract(zip,extracted);
            var bundle=Directory.GetDirectories(extracted).Single();
            var module=CadFiles.Catalog(CadReload.WindowsDirectory(bundle)).Modules.Single();
            var registration=BundledCatalog.Read().Repositories.SelectMany(r=>r.Packages).Single(p=>CadFeaturePackages.IsFeature(p)&&p.ModuleId==module.Id);
            service.Cache(registration,new(bundle,"v"+module.Version));
            request=service.Set(registration,new(bundle,module.Version),true,null);packages.Add(registration);
            selected.Add(module.Id+":"+module.Actions.Single().Id);
            if(!request.SelectedActions.Order().SequenceEqual(selected.Order()))throw new IOException("Independent package lost prior selections");
        }
        var removal=service.Set(packages[0],null,false,null);
        if(removal.SelectedActions.Count!=3)throw new IOException("Independent removal changed other buttons");
        // Restore only the selected command, using its verified cache; other versions remain unchanged.
        request=service.Set(packages[0],service.Cached(packages[0]),true,null);
        File.WriteAllText(Path.Combine(directory,"smoke-info.json"),JsonSerializer.Serialize(new {Passed=true,Data=Path.Combine(paths.Data,"Cad"),Bundle=paths.Target(Catalog.Cad),Request=request.Id,Selected=request.SelectedActions}));
    }
}
