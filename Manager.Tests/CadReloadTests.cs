using RoBIM.Cad.Configuration;
using RoBIM.Manager.Core;

internal static class CadReloadTests
{
    public static void Run(string testRoot, Action<string, Action> test)
    {
        static void Require(bool value, string message) { if (!value) throw new Exception(message); }
        static void Reject(Action action) { try { action(); } catch { return; } throw new Exception("Unexpected success"); }
        var directory = Path.Combine(testRoot, "cad-bridge");
        var paths = new ManagerPaths(Path.Combine(directory, "data"), Path.Combine(directory, "cad"), Path.Combine(directory, "revit"));
        var source = Path.Combine(directory, "source");
        var windows = CadReload.WindowsDirectory(source); Directory.CreateDirectory(windows);
        File.WriteAllText(Path.Combine(windows, "RoBIM.Cad.Loader.dll"), "fixed loader");
        File.WriteAllText(Path.Combine(windows, "tool.dll"), "feature A");
        File.WriteAllText(Path.Combine(source, "PackageContents.xml"), "<ApplicationPackage><Components><ComponentEntry ModuleName=\"./Contents/Windows/RoBIM.Cad.Loader.dll\" /></Components></ApplicationPackage>");
        var catalog = new CadCatalog { Modules = [new CadModule { Id = "tool", Name = "Tool", Assembly = "tool.dll", AssemblyName = "Tool", EntryType = "Tool.Entry",
            Actions = [new CadAction { Id = "first", Title = "First", ExecuteKey = "ONE" }, new CadAction { Id = "second", Title = "Second", ExecuteKey = "TWO" }] }] };
        CadFiles.Save(Path.Combine(windows, CadFiles.ManifestName), catalog);
        var installer = new InstallService(paths); installer.Install(Catalog.Cad, new(source, "bridge-test"));
        var installed = paths.Target(Catalog.Cad); var baseline = SafeFiles.Hashes(installed);
        var reload = new CadReload(paths,()=>[new(123,456,"fixture")]); CadRequest? first = null;
        test("CadPrepareKeepsInstalledFilesAndStartsPending", () =>
        {
            first = reload.Prepare(Catalog.Cad, source, catalog, ["tool:first"], null);
            Require(SafeFiles.Same(baseline, SafeFiles.Hashes(installed)), "Preparing modified installed package");
            Require(!installer.Status(Catalog.Cad).Modified && reload.Status(installed).Contains("준비됨"), "Wrong pending state");
            Require(!File.Exists(Path.Combine(reload.Data, "active.json")), "Desktop applied request itself");
        });
        test("CadPreparedSelectionAndRenamePreserved", () =>
        {
            var changed = catalog with { Modules = [catalog.Modules[0] with { Actions = [catalog.Modules[0].Actions[0] with { Title = "Renamed", Panel = "내 도구", Order = 7 }, catalog.Modules[0].Actions[1]] }] };
            var request = reload.Prepare(Catalog.Cad, source, changed, ["tool:first"], null);
            var snapshot = CadFiles.Snapshot(reload.Data, request);
            Require(CadFiles.Catalog(snapshot).Modules[0].Actions[0].Title == "Renamed" && request.SelectedActions.Count == 1, "Selection/label not preserved");
            // Reopen the prepared payload rather than falling back to the older installed catalog.
            var next = reload.Prepare(Catalog.Cad, installed, changed, [], null, request);
            Require(next.SelectedActions.Count == 0 && CadFiles.Catalog(CadFiles.Snapshot(reload.Data, next)).Modules[0].Actions[0].Title == "Renamed", "Prepared source lost");
        });
        test("CadReceiptRequiredForAppliedStatus", () =>
        {
            var request = reload.Desired(installed)!;
            Require(reload.Status(installed).Contains("준비됨"), "Pending shown as applied");
            CadFiles.Save(CadFiles.Child(reload.Data, "Results/" + request.Id + ".json"), new CadReceipt(request.Id, "applied", "ok", 123, 456, DateTimeOffset.UtcNow));
            Require(reload.Status(installed).Contains("적용 완료"), "CAD response not displayed");
        });
        test("CadCoreUpdateRequiresClosedHostInstall", () =>
        {
            File.WriteAllText(Path.Combine(windows, "RoBIM.Cad.Loader.dll"), "new loader");
            Reject(() => reload.Prepare(Catalog.Cad, source, catalog, [], null));
            File.WriteAllText(Path.Combine(windows, "RoBIM.Cad.Loader.dll"), "fixed loader");
        });
        test("CadSnapshotButtonRemovalDoesNotLookForLoaderInSnapshot", () =>
        {
            reload.Prepare(Catalog.Cad, source, catalog, ["tool:first", "tool:second"], null);
            var payload = reload.Source(installed, preferInstalled: true)!;
            Require(!File.Exists(Path.Combine(payload.Root, "RoBIM.Cad.Loader.dll")), "Fixture unexpectedly includes stable loader");
            Require(!payload.NeedsConnection(installer.Status(Catalog.Cad)), "Snapshot removal incorrectly requires loader update");
            var remove = reload.Toggle(Catalog.Cad, payload, "tool:first", false, null);
            Require(remove.SelectedActions.SequenceEqual(["tool:second"]), "Removal lost other installed command");
            var readd = reload.Toggle(Catalog.Cad, reload.Source(installed, preferInstalled: true)!, "tool:first", true, null);
            Require(readd.SelectedActions.ToHashSet().SetEquals(["tool:first", "tool:second"]), "Reinstallation lost previous selection");
            Require(SafeFiles.Same(baseline, SafeFiles.Hashes(installed)), "Button toggle mutated installed shared loader");
        });
        test("CadSnapshotCannotBypassChangedStableLoader", () =>
        {
            var payload = reload.Source(installed, preferInstalled: true)!;
            var path = Path.Combine(CadReload.WindowsDirectory(installed), "RoBIM.Cad.Loader.dll");
            File.WriteAllText(path, "different stable loader");
            Require(payload.NeedsConnection(installer.Status(Catalog.Cad)), "Changed loader accepted");
            Reject(() => reload.Toggle(Catalog.Cad, payload, "tool:first", false, null));
            File.WriteAllText(path, "fixed loader");
        });
        test("CadExistingButtonToggleUsesInstalledPayloadDespiteNewerDownloadedLoader", () =>
        {
            var loader = Path.Combine(windows, "RoBIM.Cad.Loader.dll"); File.WriteAllText(loader, "new downloaded loader");
            var current = reload.SourceForAction(installed, "tool:first", new(source, "downloaded"))!;
            Require(current.Previous is not null && !current.NeedsConnection(installer.Status(Catalog.Cad)), "Downloaded loader incorrectly blocks existing action");
            var removed = reload.Toggle(Catalog.Cad, current, "tool:first", false, null);
            Require(removed.SelectedActions.SequenceEqual(["tool:second"]), "Existing action removal lost another selection");
            File.WriteAllText(loader, "fixed loader");
        });
        test("CadTamperedSnapshotRejected", () =>
        {
            File.AppendAllText(CadFiles.Child(reload.Data, "Snapshots/" + first!.Id + "/tool.dll"), "tampered");
            Reject(() => CadFiles.Snapshot(reload.Data, first));
        });
        test("CadUnknownSelectionAndDuplicateIdsRejected", () =>
        {
            Reject(() => reload.Prepare(Catalog.Cad, source, catalog, ["tool:missing"], null));
            Reject(() => reload.Prepare(Catalog.Cad, source, catalog with { Modules = [catalog.Modules[0], catalog.Modules[0]] }, [], null));
        });
        test("CadTraversalAndStaleSessionRejected", () =>
        {
            Reject(() => reload.Prepare(Catalog.Cad, source, catalog with { Modules = [catalog.Modules[0] with { Assembly = "../tool.dll" }] }, [], null));
            Reject(() => reload.Prepare(Catalog.Cad, source, catalog, [], new(999999999, 1, "stale")));
        });
        test("CadCorruptPendingCanBeReprepared", () =>
        {
            File.WriteAllText(Path.Combine(reload.Data, "pending.json"), "{broken");
            Require(reload.Desired(installed) is null, "Invalid pending record blocked recovery");
            var repaired = reload.Prepare(Catalog.Cad, source, catalog, ["tool:first"], null);
            Require(reload.Desired(installed)?.Id == repaired.Id, "Pending recovery failed");
        });
        test("CadLocalModificationPreserved", () =>
        {
            File.WriteAllText(Path.Combine(installed, "personal.txt"), "keep");
            Reject(() => reload.Prepare(Catalog.Cad, source, catalog, [], null));
            Require(File.ReadAllText(Path.Combine(installed, "personal.txt")) == "keep", "Local file changed");
        });
    }
}
