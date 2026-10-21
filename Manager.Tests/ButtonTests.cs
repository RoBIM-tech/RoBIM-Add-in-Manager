using System.Text.Json;
using RoBIM.Manager.Core;
using RoBIM.Revit.Reload;

internal static class ButtonTests
{
    internal static void Run(Action<string, Action> test, Func<string, ManagerPaths> paths, Action<bool, string> require, string root)
    {
        void Reject(Action operation) { var rejected = false; try { operation(); } catch { rejected = true; } require(rejected, "Expected rejection"); }
        string RevitSource(string name)
        {
            var source = Path.Combine(root, name); Directory.CreateDirectory(Path.Combine(source, "lib")); File.WriteAllText(Path.Combine(source, "lib", "shared.py"), "preserve shared dependencies");
            var panel = Path.Combine(source, "RoBIM 기본.tab", "Elements.panel"); Directory.CreateDirectory(panel);
            File.WriteAllText(Path.Combine(source, "bundle.yaml"), "layout:\n  - RoBIM 기본\n");
            File.WriteAllText(Path.Combine(source, "RoBIM 기본.tab", "bundle.yaml"), "title: 기본\nlayout:\n  - Elements\n");
            File.WriteAllText(Path.Combine(panel, "bundle.yaml"), "title: 요소 수정\nlayout:\n  - AutoJoin\n  - Beam\nengine:\n  persistent: true\n");
            foreach (var id in new[] { "AutoJoin", "Beam", "Unpublished" })
            {
                var button = Path.Combine(panel, id + ".pushbutton"); Directory.CreateDirectory(button); File.WriteAllText(Path.Combine(button, "script.py"), "# never execute for discovery");
                File.WriteAllText(Path.Combine(button, "bundle.yaml"), id == "Beam" ? "title:\n  ko: \"보 슬래브\\n높이 맞춤\"\n  en_us: Beam\ntooltip: |\n  이동량을 검토합니다.\n  선택한 보를 수정합니다.\n" : "title: " + id + "\ntooltip: \"기능 설명: 안전한 검토\"\n");
            }
            var archive = Path.Combine(source, "archive", "Old.tab", "Tools.panel", "Old.pushbutton"); Directory.CreateDirectory(archive); File.WriteAllText(Path.Combine(archive, "script.py"), "archive");
            return source;
        }
        test("ButtonCatalogReadsLocalizedTitleAndDescription", () =>
        {
            var buttons = RevitButtons.Read(RevitSource("button-metadata")); require(buttons.Length == 2 && buttons.Any(x => x.Title == "보 슬래브 높이 맞춤" && x.Description.Contains("이동량")), "Metadata localization/folding");
            require(buttons.All(x => !x.Id.Contains("archive") && !x.Id.Contains("Unpublished")), "Archive and unpublished layout exclusion");
        });
        test("ButtonSelectionPreservesOriginalAndDependencies", () =>
        {
            var source = RevitSource("button-preservation"); var before = SafeFiles.Hashes(source); var button = RevitButtons.Read(source)[0];
            var package = RevitButtons.Prepare(new(source, "v1"), source + "-selected", [button.Id]);
            require(SafeFiles.Same(before, SafeFiles.Hashes(source)), "Source mutated"); require(File.Exists(Path.Combine(package.Path, "lib", "shared.py")), "Shared library removed");
            require(RevitButtons.Read(package.Path).Length == 2 && RevitButtons.ReadSelection(package.Path)!.Selected.SequenceEqual([button.Id]), "Removed button unavailable for reinstallation");
            var map = RevitButtons.Metadata(Path.Combine(package.Path, "RoBIM 기본.tab", "Elements.panel")); require(map.Children.Keys.Any(x => x.ToString() == "engine"), "Other YAML metadata lost");
        });
        test("SelectedProfileHidesUnlistedTestContainers", () =>
        {
            var source = RevitSource("button-test-hide");
            var command = Path.Combine(source, "RoBIM TEST.tab", "Personal.panel", "Draft.pushbutton");
            Directory.CreateDirectory(command); File.WriteAllText(Path.Combine(command, "script.py"), "# draft preserved");
            var ids = RevitButtons.Read(source).Select(x => x.Id).ToArray();
            var package = RevitButtons.Prepare(new(source, "v1"), source + "-selected", ids);
            require(!Directory.Exists(Path.Combine(package.Path, "RoBIM TEST.tab/Personal.panel/Draft.pushbutton")) && File.Exists(RevitButtons.ResourcePath(package.Path, "RoBIM TEST.tab/Personal.panel/Draft.pushbutton/script.py")), "Draft source not safely archived outside ribbon tree");
            require(File.ReadAllText(Path.Combine(package.Path, "RoBIM TEST.tab/bundle.yaml")).Contains("__ROBIM_NO_SELECTED_BUTTONS__") && File.ReadAllText(Path.Combine(package.Path, "RoBIM TEST.tab/Personal.panel/bundle.yaml")).Contains("__ROBIM_NO_SELECTED_BUTTONS__"), "Unlisted TEST containers remain visible");
        });
        test("EmptyButtonSelectionDoesNotShowAll", () =>
        {
            var source = RevitSource("button-empty"); var package = RevitButtons.Prepare(new(source, "v1"), source + "-empty", []);
            require(File.ReadAllText(Path.Combine(package.Path, "bundle.yaml")).Contains("__ROBIM_NO_SELECTED_BUTTONS__"), "Empty layout would expose every button");
            require(RevitButtons.ReadSelection(package.Path)!.Selected.Length == 0, "Empty state");
            require(!Directory.Exists(Path.Combine(package.Path, ".robim-disabled")), "Uninstalled scripts remain inside installation");
            require(!Directory.EnumerateDirectories(Path.Combine(package.Path, "RoBIM 기본.tab/Elements.panel"), "*.pushbutton").Any(), "Uninstalled commands remain in active tree");
            var id = RevitButtons.Read(package.Path)[0].Id;
            var enabled = RevitButtons.Prepare(package, source + "-reenabled", [id]);
            require(File.Exists(Path.Combine(enabled.Path, id, "script.py")) && RevitButtons.ReadSelection(enabled.Path)!.Selected.SequenceEqual([id]), "Archived button cannot be reinstalled");
        });
        test("IronPythonDevelopmentProjectStaysInCacheAndDoesNotBlockButtonReload", () =>
        {
            var source = RevitSource("button-test-harness");
            var beam = RevitButtons.Read(source).Single(x => x.Id.EndsWith("/Beam.pushbutton")).Id;
            var harness = Path.Combine(source, beam, "tests", "ironpython"); Directory.CreateDirectory(harness);
            File.WriteAllText(Path.Combine(harness, "RuntimeCheck.csproj"), "<Project />");
            File.WriteAllText(Path.Combine(harness, "Program.cs"), "// development harness");
            File.WriteAllText(Path.Combine(source, beam, "alignment_math.py"), "# runtime dependency");
            var empty = RevitButtons.Prepare(new(source, "v1"), source + "-empty", []);
            var enabled = RevitButtons.Prepare(empty, source + "-enabled", [beam]);
            require(!Directory.Exists(Path.Combine(enabled.Path, beam, "tests", "ironpython")), "Development harness installed");
            require(File.Exists(Path.Combine(RevitButtons.ReadSelection(enabled.Path)!.Cache!.Root, beam, "tests", "ironpython", "Program.cs")), "Original test source lost");
            require(File.Exists(Path.Combine(enabled.Path, beam, "alignment_math.py")), "Runtime helper removed");
            RevitReload.EnsureReloadable(SafeFiles.Hashes(empty.Path), SafeFiles.Hashes(enabled.Path));
            var removed = RevitButtons.Prepare(enabled, source + "-removed", []);
            RevitReload.EnsureReloadable(SafeFiles.Hashes(enabled.Path), SafeFiles.Hashes(removed.Path));
            Reject(() => RevitReload.EnsureReloadable(new(), new() { [beam + "/runtime.dll"] = "changed" }));
            Reject(() => RevitReload.EnsureReloadable(new(), new() { [beam + "/script.cs"] = "changed" }));
        });
        test("ClosedHostCombinesPreparedSelectionWithNextChoice", () =>
        {
            var target = paths("closed-pending-choices"); var source = RevitSource("closed-pending-source");
            var ids = RevitButtons.Read(source).Select(x => x.Id).ToArray();
            var empty = RevitButtons.Prepare(new(source, "v1"), source + "-empty", []);
            var service = new InstallService(target); service.Install(Catalog.RevitSelected, empty);
            var reload = new RevitReload(target); reload.PrepareSelection(new(target.Target(Catalog.RevitSelected), "v1"), ids[0], true, []);
            var pending = reload.PendingSelection()!; var activated = false;
            new RevitButtonInstaller(target, () => { }).Set(pending, ids[1], true, [ids[0]], () => { activated = true; return Task.CompletedTask; }).GetAwaiter().GetResult();
            require(activated && reload.PendingSelection() is null, "Closed-host pending request not consumed");
            require(RevitButtons.ReadSelection(target.Target(Catalog.RevitSelected))!.Selected.ToHashSet().SetEquals(ids), "Prior prepared choice lost");
        });
        test("ClosedHostCanApplyPreparedChoicesWithoutAddingAnotherButton", () =>
        {
            var target = paths("closed-apply-pending"); var source = RevitSource("closed-apply-pending-source");
            var id = RevitButtons.Read(source).First().Id; var empty = RevitButtons.Prepare(new(source, "v1"), source + "-empty", []);
            new InstallService(target).Install(Catalog.RevitSelected, empty); var reload = new RevitReload(target);
            reload.PrepareSelection(new(target.Target(Catalog.RevitSelected), "v1"), id, true, []);
            require(reload.ApplyPendingSelectionWhenClosed(() => Task.CompletedTask, () => { }).GetAwaiter().GetResult(), "Pending choice not applied");
            require(reload.PendingSelection() is null && RevitButtons.ReadSelection(target.Target(Catalog.RevitSelected))!.Selected.SequenceEqual([id]), "Apply-only changed prepared choices");
        });
        test("ClosedHostPendingFailureAndStaleInstallPreserveRequest", () =>
        {
            var target = paths("closed-pending-retry"); var source = RevitSource("closed-pending-retry-source");
            var ids = RevitButtons.Read(source).Select(x => x.Id).ToArray();
            var empty = RevitButtons.Prepare(new(source, "v1"), source + "-empty", []);
            var service = new InstallService(target); service.Install(Catalog.RevitSelected, empty);
            var reload = new RevitReload(target); reload.PrepareSelection(new(target.Target(Catalog.RevitSelected), "v1"), ids[0], true, []);
            var pending = reload.PendingSelection()!;
            Reject(() => new RevitButtonInstaller(target, () => { }).Set(pending, ids[1], true, [ids[0]], () => throw new IOException("Activation failed")).GetAwaiter().GetResult());
            require(reload.PendingSelection() is not null && RevitButtons.ReadSelection(target.Target(Catalog.RevitSelected))!.Selected.Length == 0, "Failed pending choice lost or installation not restored");
            File.WriteAllText(Path.Combine(target.Target(Catalog.RevitSelected), "lib", "personal.py"), "keep");
            Reject(() => new RevitButtonInstaller(target, () => { }).Set(pending, ids[1], true, [ids[0]], () => Task.CompletedTask).GetAwaiter().GetResult());
            require(reload.PendingSelection() is not null && File.Exists(Path.Combine(target.Target(Catalog.RevitSelected), "lib", "personal.py")), "Stale pending request overwrote local changes");
        });
        test("DeletedButtonCanReinstallOnlyFromVerifiedSeparateCache", () =>
        {
            var source = RevitSource("button-cache-verify"); var package = RevitButtons.Prepare(new(source, "v1"), source + "-empty", []);
            var state = RevitButtons.ReadSelection(package.Path)!; var id = state.Buttons[0].Id;
            require(!Directory.Exists(Path.Combine(package.Path, id)) && state.Cache is not null && !state.Cache.Root.StartsWith(package.Path + Path.DirectorySeparatorChar), "Removal did not delete the installed bundle");
            File.AppendAllText(Path.Combine(state.Cache!.Root, "lib/shared.py"), "tampered");
            Reject(() => RevitButtons.Prepare(package, source + "-bad-reinstall", [id]));
            require(!Directory.Exists(source + "-bad-reinstall"), "Tampered cache modified installation staging");
        });
        test("ButtonSelectionRejectsTraversalAndUnknownIds", () =>
        {
            var source = RevitSource("button-invalid"); Reject(() => RevitButtons.Prepare(new(source, "v1"), source + "-bad", ["../unsafe.pushbutton"]));
            var id = RevitButtons.Read(source)[0].Id; Reject(() => RevitButtons.Prepare(new(source, "v1"), source + "-duplicate", [id, id]));
        });
        test("ButtonYamlAliasRejectedWithoutExecution", () =>
        {
            var source = RevitSource("button-yaml"); File.WriteAllText(Path.Combine(source, "bundle.yaml"), "a: &a [one]\nlayout: *a\n"); Reject(() => RevitButtons.Read(source));
        });
        test("ButtonDescriptionReadsPythonLiteralWithoutImport", () =>
        {
            var source = RevitSource("button-script-doc"); var path = Path.Combine(source, "RoBIM 기본.tab/Elements.panel/AutoJoin.pushbutton");
            File.WriteAllText(Path.Combine(path, "bundle.yaml"), "title: AutoJoin\n");
            File.WriteAllText(Path.Combine(path, "script.py"), "__doc__ = u\"\"\"기능 설명\\n두 번째 줄\"\"\"\nraise Exception('must never execute')\n");
            require(RevitButtons.Read(source).First(x => x.Title == "AutoJoin").Description == "기능 설명 두 번째 줄", "Python literal description was not parsed safely");
        });
        test("ButtonInstallActivationFailureRestoresPriorSelection", () =>
        {
            var target = paths("button-rollback"); var source = RevitSource("button-rollback-source"); var ids = RevitButtons.Read(source).Select(x => x.Id).ToArray();
            var installer = new RevitButtonInstaller(target, () => { });
            installer.Set(new(source, "v1"), ids[0], true, [], () => Task.CompletedTask).GetAwaiter().GetResult();
            var prior = SafeFiles.Hashes(target.Target(Catalog.RevitSelected));
            Reject(() => installer.Set(new(target.Target(Catalog.RevitSelected), "v1"), ids[1], true, [ids[0]], () => throw new IOException("activation failed")).GetAwaiter().GetResult());
            require(SafeFiles.Same(prior, SafeFiles.Hashes(target.Target(Catalog.RevitSelected))), "Prior selection not restored");
        });
        test("ButtonInstallProtectsLocallyModifiedSelectedProfile", () =>
        {
            var target = paths("button-dirty"); var source = RevitSource("button-dirty-source"); var id = RevitButtons.Read(source)[0].Id; var installer = new RevitButtonInstaller(target, () => { });
            installer.Set(new(source, "v1"), id, true, [], () => Task.CompletedTask).GetAwaiter().GetResult();
            File.AppendAllText(Path.Combine(target.Target(Catalog.RevitSelected), "lib", "shared.py"), "user edit");
            Reject(() => installer.Set(new(source, "v1"), id, false, [id], () => Task.CompletedTask).GetAwaiter().GetResult());
            require(File.ReadAllText(Path.Combine(target.Target(Catalog.RevitSelected), "lib", "shared.py")).Contains("user edit"), "User edit lost");
        });
        test("SelectedProfileMutuallyExclusiveAndSwitchBack", () =>
        {
            var target = paths("button-profile"); var source = RevitSource("button-profile-source"); var package = RevitButtons.Prepare(new(source, "1"), source + "-prepared", []); new InstallService(target).Install(Catalog.RevitSelected, package);
            var flags = new Dictionary<string, bool> { ["RoBIM"] = true, ["RoBIM-Developer"] = false, ["RoBIM-Selected"] = false };
            var profiles = new RevitProfiles(target, () => { }, (name, enabled) => { flags[name] = enabled; return Task.CompletedTask; }, name => flags.GetValueOrDefault(name));
            profiles.ApplySelection().GetAwaiter().GetResult(); require(flags["RoBIM-Selected"] && !flags["RoBIM"] && !flags["RoBIM-Developer"], "Multiple active profiles");
            profiles.Apply(false).GetAwaiter().GetResult(); require(flags["RoBIM"] && !flags["RoBIM-Selected"], "Switch back leaves duplicate profile");
        });
        test("ButtonLivePrepareCoalescesWithoutChangingSourceOrInstallation", () =>
        {
            var target = paths("button-live-prepare"); var source = RevitSource("button-live-prepare-source");
            new InstallService(target).Install(Catalog.Revit, new(source, "v1")); source = target.Target(Catalog.Revit);
            var before = SafeFiles.Hashes(source); var ids = RevitButtons.Read(source).Select(x => x.Id).ToArray(); var reload = new RevitReload(target);
            reload.PrepareSelection(new(source, "v1"), ids[0], false, ids);
            require(!Directory.Exists(target.Target(Catalog.RevitSelected)) && SafeFiles.Same(before, SafeFiles.Hashes(source)), "Prepare changed a live installation");
            var pending = reload.PendingSelection()!; var selected = RevitButtons.ReadSelection(pending.Path)!.Selected;
            reload.PrepareSelection(pending, ids[1], false, selected);
            require(RevitButtons.ReadSelection(reload.PendingSelection()!.Path)!.Selected.Length == 0, "Second selection lost the first change");
            var activated = false; reload.Apply(() => { }, applySelection: () => { activated = true; return Task.CompletedTask; }).GetAwaiter().GetResult();
            require(activated && reload.PendingSelection() is null && RevitButtons.ReadSelection(target.Target(Catalog.RevitSelected))!.Selected.Length == 0, "Selected profile not applied");
            require(SafeFiles.Same(before, SafeFiles.Hashes(source)), "Original source mutated during apply");
        });
        test("ButtonLiveBootstrapPreservesLoadedBinaryBytesAndRejectsBinaryUpdates", () =>
        {
            var target = paths("button-live-binary"); var source = RevitSource("button-live-binary-source");
            File.WriteAllText(Path.Combine(source, "lib", "existing.dll"), "unchanged loaded binary");
            new InstallService(target).Install(Catalog.Revit, new(source, "v1")); source = target.Target(Catalog.Revit);
            var ids = RevitButtons.Read(source).Select(x => x.Id).ToArray(); var reload = new RevitReload(target);
            reload.PrepareSelection(new(source, "v1"), ids[0], false, ids);
            reload.Apply(() => { }, applySelection: () => Task.CompletedTask).GetAwaiter().GetResult();
            var installed = target.Target(Catalog.RevitSelected);
            require(File.ReadAllText(Path.Combine(installed, "lib", "existing.dll")) == "unchanged loaded binary", "Bootstrap changed DLL bytes");
            var changed = source + "-changed"; SafeFiles.Copy(installed, changed); File.WriteAllText(Path.Combine(changed, "lib", "existing.dll"), "different binary");
            Reject(() => reload.PrepareSelection(new(changed, "v2"), ids[0], true, [ids[1]]));
            require(reload.PendingSelection() is null, "Rejected binary update left a request");
            reload.PrepareSelection(new(installed, "v1"), ids[0], true, [ids[1]]);
            reload.Apply(() => { }, applySelection: () => Task.CompletedTask).GetAwaiter().GetResult();
            require(RevitButtons.ReadSelection(installed)!.Selected.Length == 2, "Existing selected profile did not reload");
        });
        test("ButtonLiveFailedActivationRestoresAndCanRetry", () =>
        {
            var target = paths("button-live-rollback"); var source = RevitSource("button-live-rollback-source");
            new InstallService(target).Install(Catalog.Revit, new(source, "v1")); source = target.Target(Catalog.Revit);
            var ids = RevitButtons.Read(source).Select(x => x.Id).ToArray(); var reload = new RevitReload(target);
            reload.PrepareSelection(new(source, "v1"), ids[0], false, ids);
            Reject(() => reload.Apply(() => { }, applySelection: () => throw new IOException("activation failed")).GetAwaiter().GetResult());
            require(!Directory.Exists(target.Target(Catalog.RevitSelected)) && reload.PendingSelection() is not null, "Bootstrap rollback lost pending state");
            reload.Apply(() => { }, applySelection: () => Task.CompletedTask).GetAwaiter().GetResult();
            var installed = target.Target(Catalog.RevitSelected); var before = SafeFiles.Hashes(installed);
            reload.PrepareSelection(new(installed, "v1"), ids[1], false, [ids[1]]);
            Reject(() => reload.Apply(() => { }, applySelection: () => throw new IOException("activation failed")).GetAwaiter().GetResult());
            require(SafeFiles.Same(before, SafeFiles.Hashes(installed)), "Prior selected files not restored");
            reload.Apply(() => { }, applySelection: () => Task.CompletedTask).GetAwaiter().GetResult();
            require(RevitButtons.ReadSelection(installed)!.Selected.Length == 0, "Retry failed");
        });
        test("ButtonLiveRejectsStaleSourceTamperedPayloadAndInvalidSession", () =>
        {
            var target = paths("button-live-validation"); var source = RevitSource("button-live-validation-source");
            new InstallService(target).Install(Catalog.Revit, new(source, "v1")); source = target.Target(Catalog.Revit);
            var ids = RevitButtons.Read(source).Select(x => x.Id).ToArray(); var reload = new RevitReload(target);
            reload.PrepareSelection(new(source, "v1"), ids[0], false, ids);
            Reject(() => reload.Apply(() => throw new IOException("wrong session"), applySelection: () => Task.CompletedTask).GetAwaiter().GetResult());
            require(!Directory.Exists(target.Target(Catalog.RevitSelected)), "Wrong session mutated installation");
            File.AppendAllText(Path.Combine(source, "lib", "shared.py"), "source edit");
            Reject(() => reload.Apply(() => { }, applySelection: () => Task.CompletedTask).GetAwaiter().GetResult());
            var pending = reload.PendingSelection()!; File.AppendAllText(Path.Combine(pending.Path, "lib", "shared.py"), "tamper");
            Reject(() => reload.PendingSelection());
            Reject(() => reload.Apply(() => { }, applySelection: () => Task.CompletedTask).GetAwaiter().GetResult());
            require(!Directory.Exists(target.Target(Catalog.RevitSelected)), "Invalid selection installed");
        });
        test("ButtonLiveRequestsCannotSilentlyReplaceOtherPreparedChanges", () =>
        {
            var target = paths("button-live-conflict"); var source = RevitSource("button-live-conflict-source");
            var ids = RevitButtons.Read(source).Select(x => x.Id).ToArray(); var reload = new RevitReload(target);
            reload.PrepareProfile(true); Reject(() => reload.PrepareSelection(new(source, "v1"), ids[0], false, ids));
            File.Delete(Path.Combine(target.Data, "reload-profile.json"));
            reload.Prepare(Catalog.Revit, new(source, "v1")); Reject(() => reload.PrepareSelection(new(source, "v1"), ids[0], false, ids));
            reload.DiscardPendingFor(Catalog.Revit.Id); reload.PrepareSelection(new(source, "v1"), ids[0], false, ids);
            Reject(() => reload.Prepare(Catalog.Revit, new(source, "v1"))); Reject(() => reload.PrepareProfile(false));
        });
        test("ExternalInstallationRemainsSelectedWhenGitMetadataFails", () =>
        {
            var target = paths("button-external-metadata"); var source = RevitSource("button-external-metadata-source");
            var status = EnvironmentProbe.RevitStatus(target, () => Task.FromResult<string?>(source), _ => throw new IOException("Git metadata unavailable")).GetAwaiter().GetResult();
            require(status.External && status.Path == source && status.Modified, "Existing source lost after Git failure");
            var inventory = RevitButtonInventory.Read(target, status, isEnabled: name => name == "RoBIM");
            require(inventory.Selected.Length == 2 && inventory.Root == source, "Existing buttons mislabeled as uninstalled");
        });
        test("InstallationPresenceDoesNotDependOnManagerOwnershipRecord", () =>
        {
            var target = paths("button-existing-unrecorded"); var source = RevitSource("button-existing-unrecorded-source");
            var status = new ToolStatus(Catalog.Revit, source, "unknown", false, false, false, "");
            var inventory = RevitButtonInventory.Read(target, status, isEnabled: name => name == "RoBIM");
            require(inventory.Selected.Length == 2, "Unrecorded existing installation shown as missing");
            require(RevitButtonInventory.Read(target, status, isEnabled: _ => false).Selected.Length == 0, "Disabled profile treated as active");
        });
        test("DownloadedCatalogIsNotAnInstalledProfile", () =>
        {
            var target = paths("button-download-only"); var source = RevitSource("button-download-only-source");
            var status = new InstallService(target).Status(Catalog.Revit);
            var inventory = RevitButtonInventory.Read(target, status, new(source, "v1"), _ => true);
            require(inventory.Buttons.Length == 2 && inventory.Selected.Length == 0, "Downloaded package treated as installed");
        });
        test("ConfiguredExtensionRootsReadOnlyWithoutCredentialFields", () =>
        {
            var source = RevitSource("button-config-source"); var ini = Path.Combine(root, "button-config.ini");
            var text = "[core]\nuserextensions = " + JsonSerializer.Serialize(new[] { source, source, "relative-path" }) + "\n[private]\ncredential = DO_NOT_COPY_OR_RETURN\n";
            File.WriteAllText(ini, text);
            require(EnvironmentProbe.ConfiguredRevitRoots(ini).SequenceEqual([source]), "Configured roots/deduplication failed");
            require(File.ReadAllText(ini) == text, "Discovery changed user config");
            var bin = Path.Combine(root, "button-cli-path"); Directory.CreateDirectory(bin); File.WriteAllText(Path.Combine(bin, "pyrevit.exe"), "path fixture only");
            require(EnvironmentProbe.PyRevitExecutable(bin) == Path.Combine(bin, "pyrevit.exe"), "CLI absolute executable resolution failed");
        });
        string NativeSource(string name)
        {
            var source = Path.Combine(root, "native-fixtures", name); Directory.CreateDirectory(source); File.WriteAllText(Path.Combine(source, "Feature.dll"), "fixture code");
            NativeProtocol.Save(Path.Combine(source, NativeProtocol.ManifestName), new NativeModule(1, "example", "1", "command-only", [new("run", "테스트", "설명", "RoBIM 도면", "테스트", "Feature.dll", "Example.Command")])); return source;
        }
        test("NativePrepareImmutableAndButtonRemoval", () =>
        {
            var source = NativeSource("native-button"); var before = NativeProtocol.Hashes(source); var data = paths("native-button").Data;
            var first = NativeProtocol.Prepare(data, source, "example", ["run"], 0, 0); require(NativeProtocol.Same(before, NativeProtocol.Hashes(source)), "Native source modified");
            require(!first.ApplyRequested && first.Modules.Single().Selected.SequenceEqual(["run"]), "Native selection not prepared"); NativeProtocol.Validate(data, first);
            var removed = NativeProtocol.Prepare(data, source, "example", [], 0, 0); require(removed.Modules.Length == 0, "Native button removal must remove dispatch binding"); require(Directory.Exists(first.Modules[0].Directory), "Pending source removed while potentially loaded");
        });
        test("NativePrepareRejectsTamperedPayloadAndTraversal", () =>
        {
            var source = NativeSource("native-tamper"); var data = paths("native-tamper").Data; var request = NativeProtocol.Prepare(data, source, "example", ["run"], 12, 34);
            File.AppendAllText(Path.Combine(request.Modules[0].Directory, "Feature.dll"), "changed"); Reject(() => NativeProtocol.Validate(data, request));
            Reject(() => NativeProtocol.Child(data, "../outside"));
            var module = NativeProtocol.Module(source); NativeProtocol.Save(Path.Combine(source, NativeProtocol.ManifestName), module with { Commands = [module.Commands[0] with { Assembly = "../escape.dll" }] }); Reject(() => NativeProtocol.Module(source));
        });
        test("NativeSessionValidatedBeforeAndAfterPayloadValidation", () =>
        {
            var source = NativeSource("native-session"); var data = paths("native-session").Data; var request = NativeProtocol.Prepare(data, source, "example", ["run"], 12, 34); var calls = 0;
            Reject(() => NativeProtocol.Validate(data, request, () => { if (++calls == 2) throw new IOException("session changed"); })); require(calls == 2, "Missing session recheck");
        });
        test("NativeSelectionRejectsUnknownCommandsAndWrongPolicy", () =>
        {
            var source = NativeSource("native-invalid"); var data = paths("native-invalid").Data; Reject(() => NativeProtocol.Prepare(data, source, "example", ["other"], 0, 0));
            var module = NativeProtocol.Module(source); NativeProtocol.Save(Path.Combine(source, NativeProtocol.ManifestName), module with { ReloadPolicy = "startup-application" }); Reject(() => NativeProtocol.Module(source));
        });
        string Archive(string source)
        {
            var zip = Path.Combine(root, Guid.NewGuid().ToString("N") + ".zip");
            System.IO.Compression.ZipFile.CreateFromDirectory(Path.GetDirectoryName(source)!, zip);
            File.WriteAllText(zip + ".sha256", SafeFiles.Hash(zip)); return zip;
        }
        test("NativeImportToggleUpdateReloadReceiptFlow", () =>
        {
            var source = NativeSource("native-lifecycle/Example.bundle"); var target = paths("native-lifecycle"); var service = new NativeRevitCommands(target, () => new(123, 456));
            var package = service.Import(Archive(source)); require(service.Desired() is null, "Import must not install an unselected module");
            var first = service.Toggle(package.Path, "run", true); require(service.State("example", "run").Pending, "Preparation falsely shown as applied");
            File.WriteAllText(Path.Combine(source, "Feature.dll"), "new version code"); var module = NativeProtocol.Module(source); NativeProtocol.Save(Path.Combine(source, NativeProtocol.ManifestName), module with { Version = "2" });
            service.Import(Archive(source)); var second = service.Desired()!;
            require(first.Id != second.Id && second.Modules.Single().Manifest.Version == "2" && second.Modules.Single().Selected.SequenceEqual(["run"]) && !second.ApplyRequested, "Updated package not prepared preserving selection");
            service.RequestReload(); require(service.Desired()!.ApplyRequested, "Explicit apply request missing");
            var receipt = NativeProtocol.Child(service.Data, $"Results/{second.Id}-123-456.json");
            NativeProtocol.Save(receipt, new NativeReceipt(second.Id, "applied", "wrong session", 999, 456, DateTimeOffset.UtcNow)); require(service.State("example", "run").Pending, "Wrong receipt accepted");
            NativeProtocol.Save(receipt, new NativeReceipt(second.Id, "applied", "done", 123, 456, DateTimeOffset.UtcNow)); require(!service.State("example", "run").Pending, "Matching receipt not recognized");
            service.Toggle(service.Cached("example")!.Path, "run", false); require(!service.State("example", "run").Selected, "Button removal selection");
        });
        test("NativeImportCannotSilentlySwitchStableToTest", () =>
        {
            var source = NativeSource("native-channel/Example.bundle"); var target = paths("native-channel"); var service = new NativeRevitCommands(target, () => null);
            var module = NativeProtocol.Module(source); NativeProtocol.Save(Path.Combine(source,NativeProtocol.ManifestName),module with { Version = "1.0.0" });
            NativeProtocol.Save(Path.Combine(source,RoBIM.Common.ReleaseMetadata.FileName),new RoBIM.Common.ReleaseManifest(1,[new("example","1.0.0","stable",new("reviewer","fixture host","https://example.org/verification"))]));
            var first = service.Import(Archive(source)); service.Toggle(first.Path,"run",true); var before = service.Desired()!;
            NativeProtocol.Save(Path.Combine(source,NativeProtocol.ManifestName),module with { Version = "2.0.0-test.1" });
            NativeProtocol.Save(Path.Combine(source,RoBIM.Common.ReleaseMetadata.FileName),new RoBIM.Common.ReleaseManifest(1,[new("example","2.0.0-test.1","test")]));
            service.Import(Archive(source));
            require(service.Desired()!.Id == before.Id && service.Desired()!.Modules.Single().Manifest.Version == "1.0.0", "Import changed chosen release channel");
            service.Toggle(service.Cached("example")!.Path,"run",true);
            require(service.Desired()!.Modules.Single().Manifest.Version == "2.0.0-test.1", "Explicit TEST choice was rejected");
        });
        test("NativeRemovalPreservesChosenGenerationDespiteNewTestCatalog", () =>
        {
            var source = NativeSource("native-remove-generation/Example.bundle"); var service = new NativeRevitCommands(paths("native-remove-generation"), () => null);
            var module = NativeProtocol.Module(source); module = module with { Version = "1.0.0", Commands = [module.Commands[0],module.Commands[0] with { Id = "other" }] };
            NativeProtocol.Save(Path.Combine(source,NativeProtocol.ManifestName),module);
            var first = service.Cache(new(source,"1.0.0")); service.Toggle(first.Path,"run",true); service.Toggle(first.Path,"other",true);
            NativeProtocol.Save(Path.Combine(source,NativeProtocol.ManifestName),module with { Version = "2.0.0-test.1" });
            var next = service.Cache(new(source,"2.0.0-test.1")); service.Toggle(next.Path,"run",false);
            var kept = service.Desired()!.Modules.Single();
            require(kept.Manifest.Version == "1.0.0" && kept.Selected.SequenceEqual(["other"]), "Removal changed another button's chosen generation");
        });
        test("NativeModifiedCatalogCacheRejected", () =>
        {
            var source = NativeSource("native-cache/Example.bundle"); var target = paths("native-cache"); var service = new NativeRevitCommands(target, () => null); var package = service.Import(Archive(source));
            File.AppendAllText(Path.Combine(package.Path, "Feature.dll"), "local change"); Reject(() => service.Toggle(package.Path, "run", true)); require(service.Desired() is null, "Modified cache was prepared");
        });
        test("NativeBatchUpdateDoesNotPublishPartialFailure", () =>
        {
            var target = paths("native-batch"); var service = new NativeRevitCommands(target, () => null);
            var sources = new List<string>();
            foreach (var id in new[] { "first", "second" })
            {
                var source = NativeSource("native-batch-" + id + "/Example.bundle"); var module = NativeProtocol.Module(source); NativeProtocol.Save(Path.Combine(source, NativeProtocol.ManifestName), module with { Id = id }); sources.Add(source);
                var package = service.Cache(new(source, "1")); service.Toggle(package.Path, "run", true);
            }
            var before = service.Desired()!.Id;
            foreach (var source in sources)
            {
                var module = NativeProtocol.Module(source); NativeProtocol.Save(Path.Combine(source, NativeProtocol.ManifestName), module with { Version = "2", Commands = module.Id == "second" ? [module.Commands[0] with { Id = "renamed" }] : module.Commands }); service.Cache(new(source, "2"));
            }
            Reject(service.PrepareCachedUpdates); require(service.Desired()!.Id == before && service.Desired()!.Modules.All(x => x.Manifest.Version == "1"), "Batch update published partially before validation failure");
        });
        string HostSource(string name)
        {
            var source = Path.Combine(root, "host-fixtures", name, Catalog.RevitHost.FolderName); var contents = Path.Combine(source, "Contents/2026"); Directory.CreateDirectory(contents);
            File.WriteAllText(Path.Combine(contents, "RoBIM.Revit.Host.dll"), "host v1");
            File.WriteAllText(Path.Combine(contents, "RoBIM.Revit.Host.addin"), "<RevitAddIns><AddIn Type=\"Application\"><Assembly>RoBIM.Revit.Host.dll</Assembly><AddInId>9DC5FBA5-A8BA-4C29-BD61-A8F80565033F</AddInId><FullClassName>RoBIM.Revit.Host.App</FullClassName></AddIn></RevitAddIns>");
            File.WriteAllText(Path.Combine(source, "PackageContents.xml"), "<ApplicationPackage><Components><RuntimeRequirements OS=\"Win64\" Platform=\"Revit\" SeriesMin=\"R2026\" SeriesMax=\"R2026\"/><ComponentEntry ModuleName=\"./Contents/2026/RoBIM.Revit.Host.addin\"/></Components></ApplicationPackage>"); return source;
        }
        test("NativeHostFirstInstallAndSameBinaryNeedNoClosure", () =>
        {
            var source = HostSource("first"); var target = paths("native-host-first"); var service = new NativeRevitCommands(target, () => null); var zip = Archive(source); var calls = 0;
            void Closed() { calls++; throw new IOException("host running"); }
            service.InstallHost(zip, Path.Combine(target.Data, "Addins"), Closed); service.InstallHost(zip, Path.Combine(target.Data, "Addins"), Closed);
            require(calls == 0 && new InstallService(target).Status(Catalog.RevitHost).Managed, "First bootstrap or unchanged loader wrongly blocked");
            require(File.ReadAllText(Path.Combine(target.Data, "Addins/RoBIM.NativeHost.addin")).Contains(target.Target(Catalog.RevitHost)), "Registration assembly target");
        });
        test("NativeHostChangedBinaryRequiresClosure", () =>
        {
            var source = HostSource("changed"); var target = paths("native-host-changed"); var service = new NativeRevitCommands(target, () => null); var addins = Path.Combine(target.Data, "Addins");
            service.InstallHost(Archive(source), addins, () => { }); var before = SafeFiles.Hashes(target.Target(Catalog.RevitHost)); File.AppendAllText(Path.Combine(source, "Contents/2026/RoBIM.Revit.Host.dll"), "new core");
            Reject(() => service.InstallHost(Archive(source), addins, () => throw new IOException("host running"))); require(SafeFiles.Same(before, SafeFiles.Hashes(target.Target(Catalog.RevitHost))), "Running loader overwritten");
        });
        test("NativeHostPreservesForeignRegistration", () =>
        {
            var source = HostSource("foreign"); var target = paths("native-host-foreign"); var service = new NativeRevitCommands(target, () => null); var addins = Path.Combine(target.Data, "Addins"); Directory.CreateDirectory(addins);
            var file = Path.Combine(addins, "RoBIM.NativeHost.addin"); File.WriteAllText(file, "unmanaged registration"); Reject(() => service.InstallHost(Archive(source), addins, () => { }));
            require(File.ReadAllText(file) == "unmanaged registration" && !Directory.Exists(target.Target(Catalog.RevitHost)), "Foreign registration replaced");
        });
    }
}
