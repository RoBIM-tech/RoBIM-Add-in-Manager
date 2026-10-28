using RoBIM.Manager.Core;

internal static class NativeOnlyPolicyTests
{
    public static void Run(string root, Action<string, Action> test)
    {
        void Require(bool condition) { if (!condition) throw new Exception("Native-only invariant failed"); }
        void Rejected(Action action) { try { action(); } catch (IOException) { return; } throw new Exception("Retired runtime accepted"); }
        ManagerPaths Paths(string name) => new(Path.Combine(root,name,"data"),Path.Combine(root,name,"cad"),Path.Combine(root,name,"revit"));
        test("NativePolicyIgnoresMissingAndCorruptHistoricalMode", () =>
        {
            var paths = Paths("native-policy-mode"); Directory.CreateDirectory(paths.Data);
            Require(RevitRuntimePolicy.NativeOnly(paths));
            File.WriteAllText(Path.Combine(paths.Data,"revit-mode.json"),"broken legacy configuration");
            Require(RevitRuntimePolicy.NativeOnly(paths));
            RevitRuntimePolicy.EnsureAllowed(paths,Catalog.Cad);
            RevitRuntimePolicy.EnsureAllowed(paths,Catalog.RevitHost);
        });
        test("NativePolicyRejectsLegacyInstallBeforeChangingExistingFiles", () =>
        {
            var paths = Paths("native-policy-install"); Directory.CreateDirectory(paths.Target(Catalog.ReloadBridge));
            var existing = Path.Combine(paths.Target(Catalog.ReloadBridge),"personal.txt"); File.WriteAllText(existing,"preserve existing legacy data");
            var source = Path.Combine(root,"native-policy-source"); Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source,"script.py"),"must not install");
            Rejected(() => new InstallService(paths,_ => {}).Install(Catalog.ReloadBridge,new(source,"99")));
            Require(File.ReadAllText(existing)=="preserve existing legacy data" && !File.Exists(Path.Combine(paths.Data,"installed.json")));
        });
        test("NativePolicyRejectsPyRevitRegistrationPreservesNativeRegistry", () =>
        {
            var paths = Paths("native-policy-registry"); var registry = new ExtensionCatalog(paths);
            var native = BundledCatalog.Read().Repositories.First(x => x.Packages.Length > 0 && x.Packages.All(p => p.Host == "revit"));
            registry.Register(native); var before = SafeFiles.Hashes(paths.Data);
            var legacy = new ExtensionRepository("example/legacy","main",[new("legacy","Legacy","pyrevit",".","Legacy.extension")],"historical fixture");
            Rejected(() => registry.Register(legacy));
            Require(SafeFiles.Same(before,SafeFiles.Hashes(paths.Data)));
        });
    }
}
