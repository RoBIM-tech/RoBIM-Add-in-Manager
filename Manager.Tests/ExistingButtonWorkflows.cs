using System.Text.Json;
using RoBIM.Cad.Configuration;
using RoBIM.Manager.Core;

internal static class ExistingButtonWorkflows
{
    // Read the real environment, then run selection mutations only against a new temporary copy.
    internal static async Task Run(string root)
    {
        if (Directory.Exists(root)) throw new IOException("Integration check requires a new output directory.");
        Directory.CreateDirectory(root);
        var checks = new List<string>();
        void Require(bool condition, string name) { if (!condition) throw new IOException(name); checks.Add(name); }
        var real = ManagerPaths.CurrentUser();
        var realRevit = await EnvironmentProbe.RevitStatus(real);
        var inventory = RevitButtonInventory.Read(real, realRevit);
        Require(inventory.Root is not null && inventory.Selected.Length > 0, "Actual existing pyRevit buttons detected as installed");
        var original = SafeFiles.Hashes(inventory.Root!);
        var realState = File.ReadAllText(Path.Combine(real.Data, "installed.json"));
        var realCad = new CadReload(real);
        var cadBundle = real.Target(Catalog.Cad);
        var cadBefore = SafeFiles.Hashes(cadBundle);
        var request = realCad.Desired(cadBundle) ?? throw new IOException("Actual CAD selection missing");
        var pendingFile = Path.Combine(realCad.Data, "pending.json");
        var pendingBefore = File.Exists(pendingFile) ? File.ReadAllText(pendingFile) : null;

        var copy = new ManagerPaths(Path.Combine(root, "data"), Path.Combine(root, "cad"), Path.Combine(root, "revit"));
        var installer = new InstallService(copy);
        installer.Install(Catalog.Revit, new(inventory.Root!, realRevit.Version));
        var reload = new RevitReload(copy); var first = inventory.Selected[0];
        reload.PrepareSelection(new(copy.Target(Catalog.Revit), realRevit.Version), first, false, inventory.Selected);
        await reload.Apply(() => { }, applySelection: () => Task.CompletedTask);
        var selectedRoot = copy.Target(Catalog.RevitSelected);
        var afterRemoval = RevitButtons.ReadSelection(selectedRoot)!.Selected;
        Require(afterRemoval.Length == inventory.Selected.Length - 1 && !afterRemoval.Contains(first), "Actual pyRevit payload removes one button in temporary profile");
        reload.PrepareSelection(new(selectedRoot, realRevit.Version), first, true, afterRemoval);
        await reload.Apply(() => { }, applySelection: () => Task.CompletedTask);
        Require(RevitButtons.ReadSelection(selectedRoot)!.Selected.ToHashSet().SetEquals(inventory.Selected), "Actual pyRevit payload reinstalls the button and preserves other selections");

        installer.Install(Catalog.Cad, new(cadBundle, "actual installed source"));
        var cad = new CadReload(copy);
        SafeFiles.Copy(CadFiles.Snapshot(realCad.Data, request), CadFiles.Child(cad.Data, "Snapshots/" + request.Id));
        CadFiles.Save(Path.Combine(cad.Data, "pending.json"), request with { BundlePath = copy.Target(Catalog.Cad) });
        var source = cad.Source(copy.Target(Catalog.Cad), preferInstalled: true)!;
        Require(!File.Exists(Path.Combine(source.Root, "RoBIM.Cad.Loader.dll")) && !source.NeedsConnection(installer.Status(Catalog.Cad)), "Actual CAD snapshot resolves its stable loader from the request");
        var key = request.SelectedActions[0];
        var removed = cad.Toggle(Catalog.Cad, source, key, false, null);
        Require(removed.SelectedActions.Count == request.SelectedActions.Count - 1 && !removed.SelectedActions.Contains(key), "Actual CAD snapshot prepares removal and preserves other commands");
        var restored = cad.Toggle(Catalog.Cad, cad.Source(copy.Target(Catalog.Cad), preferInstalled: true)!, key, true, null);
        Require(restored.SelectedActions.ToHashSet().SetEquals(request.SelectedActions), "Actual CAD snapshot prepares reinstallation without losing prior selection");
        Require(SafeFiles.Same(original, SafeFiles.Hashes(inventory.Root!)) && SafeFiles.Same(cadBefore, SafeFiles.Hashes(cadBundle)) &&
            File.ReadAllText(Path.Combine(real.Data, "installed.json")) == realState &&
            (File.Exists(pendingFile) ? File.ReadAllText(pendingFile) : null) == pendingBefore, "Real installation, source and CAD pending request remain unchanged");
        var result = new { Passed = checks.Count, Checks = checks, PyRevitBefore = inventory.Selected.Length, PyRevitAfterRemoval = afterRemoval.Length,
            CadBefore = request.SelectedActions.Count, CadAfterRemoval = removed.SelectedActions.Count, RealStateUnchanged = true };
        File.WriteAllText(Path.Combine(root, "results.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(result));
    }
}
