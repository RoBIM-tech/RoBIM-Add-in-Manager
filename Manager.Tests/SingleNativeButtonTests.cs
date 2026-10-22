using System.Text.Json;
using RoBIM.Manager.Core;
using RoBIM.Revit.Reload;

internal static class SingleNativeButtonTests
{
    public static void Run(Action<string,Action> test, Func<string,ManagerPaths> paths, Action<bool,string> require, string root)
    {
        var repo = BundledCatalog.Read().Repositories.Single(r => r.Repository.EndsWith("/RoBIM-Revit-ParameterCopy"));
        var package = repo.Packages.Single();
        var registration = SingleNativeButton.Registration(repo, package)!;
        SourcePackage Fixture(string name, string moduleId = "parameter-copy", string commandId = "copy", bool two = false)
        {
            var dir = Path.Combine(root, "direct-fixtures", name, "runtime"); Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "Feature.dll"), "fixture-only-no-model-execution");
            var command = new NativeCommand(commandId, "RoBIM 파라미터 복사", "description", "RoBIM TEST", "독립 도구 TEST", "Feature.dll", "Feature.Command");
            NativeProtocol.Save(Path.Combine(dir, NativeProtocol.ManifestName), new NativeModule(1, moduleId, "0.1.0-test.2", "command-only", two ? [command, command with { Id = "extra" }] : [command]));
            return new(dir,"0.1.0-test.2");
        }
        void Reject(Action action) { bool rejected=false; try {action();} catch(IOException) {rejected=true;} require(rejected,"Invalid package accepted"); }
        test("SingleNativeNamesAreDisplayOnly",()=> {
            require(ToolNames.Display("RoBIM 파라미터 복사") == "파라미터 복사" && ToolNames.Display("RoBIMatic") == "RoBIMatic", "Prefix handling changed tool names incorrectly");
            require(ExtensionCatalog.Tool(repo,package).Name == "파라미터 복사" && package.Name.StartsWith("RoBIM "), "Display cleanup mutated source descriptor");
        });
        test("SingleNativeRejectsWrongIdentityOrMultipleCommands",()=> {
            Reject(()=>SingleNativeButton.Validate(repo,package,Fixture("wrong-module","wrong")));
            Reject(()=>SingleNativeButton.Validate(repo,package,Fixture("wrong-command",commandId:"wrong")));
            Reject(()=>SingleNativeButton.Validate(repo,package,Fixture("two",two:true)));
        });
        test("ReleasePromotionDownloadsStableWithoutRelabelingInstalledTest",()=> {
            var target=paths("single-promotion");var old=Fixture("promotion-old");
            var manager=new NativeRevitCommands(target,()=>null);var cached=manager.Cache(old);var before=manager.Toggle(cached.Path,"copy",true);
            var stable=Fixture("promotion-stable");var module=NativeProtocol.Module(stable.Path) with {Version="0.1.0"};
            NativeProtocol.Save(Path.Combine(stable.Path,NativeProtocol.ManifestName),module);
            File.WriteAllText(Path.Combine(stable.Path,"robim-release.json"),JsonSerializer.Serialize(new {schemaVersion=1,modules=new[]{new {id="parameter-copy",version="0.1.0",channel="stable",validation=new {by="fixture",hostBuild="Revit 2026.5",evidence="https://example.org/validation"}}}}));
            stable=stable with {Version="0.1.0"};var descriptor=package with {Channel="stable"};int downloads=0;
            require(SingleNativeButton.Validate(repo,descriptor,cached,requireChannel:false).Version=="0.1.0-test.2","Display/removal rejected installed TEST");
            Reject(()=>SingleNativeButton.Validate(repo,descriptor,cached));
            var source=SingleNativeButton.Resolve(target,repo,descriptor,()=>{downloads++;return Task.FromResult(stable);}).GetAwaiter().GetResult();
            require(downloads==1 && ModuleReleases.Native(source.Path,"parameter-copy","0.1.0").Channel=="stable","Promotion reused old TEST cache");
            manager.PrepareCachedUpdates();
            require(manager.Desired()!.Id==before.Id && manager.Desired()!.Modules.Single().Manifest.Version=="0.1.0-test.2","Catalog update silently upgraded installed channel");
            var selected=manager.Toggle(source.Path,"copy",true);
            require(selected.Modules.Single().Manifest.Version=="0.1.0","Explicit upgrade did not select stable bytes");
        });
        test("ReleaseDescriptorRefreshAllowsChannelAndNameButRejectsIdentityChange",()=> {
            var promoted=package with {Channel="stable",Name="파라미터 복사"};
            require(ExtensionCatalog.CurrentPackage(package,repo with {Packages=[promoted]})==promoted,"Channel promotion rejected");
            Reject(()=>ExtensionCatalog.CurrentPackage(package,repo with {Packages=[promoted with {Folder="Other.bundle"}]}));
            Reject(()=>ExtensionCatalog.CurrentPackage(package,repo with {Packages=[promoted with {Asset="other.zip"}]}));
            Reject(()=>ExtensionCatalog.CurrentPackage(package,repo with {Packages=[]}));
        });
        test("SingleNativeColdInstallResolvesAndSelectsOneButton",()=> {
            var target=paths("single-cold"); int downloads=0;
            var input=Fixture("cold");
            var source=SingleNativeButton.Resolve(target,repo,package,()=>{downloads++;return Task.FromResult(input);}).GetAwaiter().GetResult();
            require(downloads==1 && NativeProtocol.Module(source.Path).Id == registration.ModuleId,"Cold install did not resolve correct button");
            var manager=new NativeRevitCommands(target,()=>null); var request=manager.Toggle(source.Path,registration.CommandId,true);
            require(request.Modules.Single().Selected.SequenceEqual(new[]{registration.CommandId}),"Install did not select the sole command");
        });
        test("SingleNativeReusesOldRuntimeCacheWithoutNetwork",()=> {
            var target=paths("single-old-cache");var input=Fixture("old-cache");
            new NativeRevitCommands(target,()=>null).Cache(input);
            var resolved=SingleNativeButton.Resolve(target,repo,package,()=>throw new Exception("Unnecessary download")).GetAwaiter().GetResult();
            require(Path.GetFileName(resolved.Path)=="runtime" && NativeProtocol.Module(resolved.Path).Id=="parameter-copy","Legacy runtime directory lost module association");
        });
        test("SingleNativeInvalidDownloadLeavesSelectionUnchanged",()=> {
            var target=paths("single-invalid");
            Reject(()=>SingleNativeButton.Resolve(target,repo,package,()=>Task.FromResult(Fixture("invalid", "other"))).GetAwaiter().GetResult());
            require(new NativeRevitCommands(target,()=>null).Desired() is null && !File.Exists(Path.Combine(target.Data,"NativeRevit","catalog-parameter-copy.json")),"Invalid download modified selection/cache");
        });
        test("InstallAddsDefaultRibbonPlacementPreservingCustomization",()=> {
            var store=new RibbonLayouts(paths("single-ribbon"));
            var custom = new RibbonLayout(1,"Revit",[new("내 탭",[new("내 패널",[new("old:a","이름 변경", "Small","stack",2),new("old:b","두 번째", "Small","stack",2)])])]); store.Save(custom);
            var added=new RibbonTool("native:parameter-copy:copy","RoBIM 파라미터 복사","RoBIM TEST","독립 도구 TEST",true);
            store.EnsureInstalled(added,[]);var after=store.Read("Revit");
            require(after.Tabs[0] == custom.Tabs[0] || JsonSerializer.Serialize(after.Tabs[0])==JsonSerializer.Serialize(custom.Tabs[0]),"Existing custom layout changed");
            require(after.Tabs[1].Panels[0].Buttons.Single().Id==added.Id && after.Tabs[1].Panels[0].Buttons.Single().Title=="파라미터 복사","Default placement missing");
            var bytes=File.ReadAllBytes(store.FilePath("Revit"));store.EnsureInstalled(added,[]);
            require(bytes.SequenceEqual(File.ReadAllBytes(store.FilePath("Revit"))),"Reinstall duplicated or overwrote placement");
        });
        test("FailedInstallRestoresRibbonAndPreservesConcurrentEdits",()=> {
            var store=new RibbonLayouts(paths("single-layout-failure"));
            var added=new RibbonTool("native:parameter-copy:copy","파라미터 복사","TEST","패널",true);
            Reject(()=>store.EnsureInstalled(added,[],()=>throw new IOException("install failed")));
            require(!File.Exists(store.FilePath("Revit")),"Failed first install left a layout file");
            store.Save(new(1,"Revit",[new("기존",[new("패널",[new("old", "기존 버튼")])])]));
            var original=File.ReadAllBytes(store.FilePath("Revit"));
            Reject(()=>store.EnsureInstalled(added,[],()=>throw new IOException("install failed")));
            require(original.SequenceEqual(File.ReadAllBytes(store.FilePath("Revit"))),"Failed install changed saved layout");
            Reject(()=>store.EnsureInstalled(added,[],()=> {store.Save(new(1,"Revit",[new("외부 수정",[])]));throw new IOException("install failed");}));
            require(store.Read("Revit").Tabs.Single().Name=="외부 수정","Rollback overwrote a concurrent edit");
        });
        test("ReinstallPreservesMovedButtonAndFirstInstallKeepsDefaults",()=> {
            var store=new RibbonLayouts(paths("single-custom"));
            var existing=new RibbonTool("native:parameter-copy:copy","내 이름","내 탭","내 패널",true);
            store.EnsureInstalled(existing,[new("other", "기존 버튼","기본 탭","기본 패널",true)]);
            store.EnsureInstalled(existing with { Tab="RoBIM TEST",Panel="독립 도구 TEST",Title="기본 이름" },[]);
            var layout=store.Read("Revit");
            require(layout.Tabs[0].Panels[0].Buttons.Single().Id=="other" && layout.Tabs[1].Name=="내 탭" && layout.Tabs[1].Panels[0].Buttons.Single().Title=="내 이름","Install replaced existing defaults or customized placement");
        });
    }

    public static async Task Downloads(string root)
    {
        Directory.CreateDirectory(root);
        var target=new ManagerPaths(Path.Combine(root,"data"),Path.Combine(root,"cad"),Path.Combine(root,"revit"));
        var results=new List<object>();
        foreach(var repo in BundledCatalog.Read().Repositories.Where(r=>r.Packages.SingleOrDefault()?.Host=="revit"))
        {
            var package=repo.Packages.Single();
            var source=await SingleNativeButton.Resolve(target,repo,package,()=>new ExtensionCatalog(target).Download(repo,package));
            var module=SingleNativeButton.Validate(repo,package,source);
            var request=new NativeRevitCommands(target,()=>null).Toggle(source.Path,module.Commands.Single().Id,true);
            new RibbonLayouts(target).EnsureInstalled(new("native:"+module.Id+":"+module.Commands[0].Id, module.Commands[0].Title, module.Commands[0].Tab,module.Commands[0].Panel,true),[]);
            results.Add(new {Repository=repo.Repository,Module=module.Id,Command=module.Commands[0].Id,Version=module.Version,Selected=request.Modules.Single(m=>m.Manifest.Id==module.Id).Selected,Folder=Path.GetFileName(source.Path),Passed=true});
        }
        File.WriteAllText(Path.Combine(root,"download-results.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
    }
}
