using System.IO.Compression;
using System.Text.Json;
using RoBIM.Manager.Core;

if (args.Length == 2 && args[0] == "--check-organization-transfer")
{
    await RepositoryOwnershipTests.Live(Path.GetFullPath(args[1])); return 0;
}

if (args.Length == 2 && args[0] == "--check-native-downloads")
{
    await SingleNativeButtonTests.Downloads(Path.GetFullPath(args[1])); return 0;
}
if (args.Length == 2 && args[0] == "--check-native-release-channels")
{
    await SingleNativeButtonTests.ReleaseChannels(Path.GetFullPath(args[1])); return 0;
}

if (args.Length == 2 && args[0] == "--check-existing-button-workflows")
{
    await ExistingButtonWorkflows.Run(Path.GetFullPath(args[1])); return 0;
}

if (args.Length == 3 && args[0] == "--prepare-cad-smoke")
{
    CadSmoke.Prepare(Path.GetFullPath(args[1]), Path.GetFullPath(args[2])); return 0;
}

var root = Path.GetFullPath(args.FirstOrDefault(x => !x.StartsWith("--")) ?? Path.Combine(Path.GetTempPath(), "RoBIM-tests-" + Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(root);
var results = new List<object>();
var failures = 0;
void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
void Reject(Action action)
{
    var rejected = false;
    try { action(); } catch (Exception) { rejected = true; }
    Require(rejected, "Operation unexpectedly succeeded");
}
ManagerPaths Paths(string name) => new(Path.Combine(root, name, "data"), Path.Combine(root, name, "cad"), Path.Combine(root, name, "revit"));
string Package(string name, string content, ToolSpec? tool = null)
{
    var path = Path.Combine(root, "packages", name);
    Directory.CreateDirectory(path);
    if (tool?.Kind == ToolKind.PyRevit)
    {
        Directory.CreateDirectory(Path.Combine(path, "RoBIM 기본.tab"));
        Directory.CreateDirectory(Path.Combine(path, "lib"));
        File.WriteAllText(Path.Combine(path, "lib", "test.py"), content);
    }
    else
    {
        Directory.CreateDirectory(Path.Combine(path, "Contents", "Windows"));
        File.WriteAllText(Path.Combine(path, "PackageContents.xml"), "<ApplicationPackage />");
        File.WriteAllText(Path.Combine(path, "Contents", "Windows", "DelHiddenNet.dll"), content);
    }
    return path;
}
void Test(string name, Action action)
{
    try { action(); results.Add(new { Name = name, Passed = true, Error = "" }); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failures++; results.Add(new { Name = name, Passed = false, Error = ex.ToString() }); Console.WriteLine("FAIL " + name + ": " + ex.Message); }
}
if (args.Contains("--discovery-only"))
{
    RepositoryDiscoveryTests.Run(root, Test);
    Console.WriteLine($"{results.Count-failures}/{results.Count} discovery checks passed"); return failures==0?0:1;
}
if (args.Contains("--uninstall-only"))
{
    ManagerUninstallTests.Run(root, Test);
    File.WriteAllText(Path.Combine(root,"results.json"),JsonSerializer.Serialize(results));
    Console.WriteLine($"{results.Count-failures}/{results.Count} uninstall checks passed"); return failures==0?0:1;
}
if (args.Contains("--team-only"))
{
    TeamAccessTests.Run(Test);
    File.WriteAllText(Path.Combine(root,"results.json"),JsonSerializer.Serialize(results));
    Console.WriteLine($"{results.Count-failures}/{results.Count} team checks passed"); return failures==0?0:1;
}
if (args.Contains("--ownership-only"))
{
    RepositoryOwnershipTests.Run(root, Test);
    File.WriteAllText(Path.Combine(root,"results.json"),JsonSerializer.Serialize(results));
    Console.WriteLine($"{results.Count-failures}/{results.Count} ownership checks passed"); return failures==0?0:1;
}
if (args.Contains("--bridge-only"))
{
    ReloadBridgeTests.Run(root,Test);
    File.WriteAllText(Path.Combine(root,"results.json"),JsonSerializer.Serialize(results));
    Console.WriteLine($"{results.Count-failures}/{results.Count} bridge checks passed"); return failures==0?0:1;
}
if (args.Contains("--buttons-only"))
{
    ButtonTests.Run(Test, Paths, Require, root);
    File.WriteAllText(Path.Combine(root,"results.json"),JsonSerializer.Serialize(results));
    Console.WriteLine($"{results.Count-failures}/{results.Count} button checks passed"); return failures==0?0:1;
}
if (args.Contains("--location-only"))
{
    RepositoryDiscoveryTests.Run(root, Test);
PyRevitLocationTests.Run(Test, Paths, Require, root);
    File.WriteAllText(Path.Combine(root,"results.json"),JsonSerializer.Serialize(results));
    Console.WriteLine($"{results.Count-failures}/{results.Count} passed"); return failures==0?0:1;
}
if (args.Contains("--native-single-only"))
{
    SingleNativeButtonTests.Run(Test, Paths, Require, root);
    File.WriteAllText(Path.Combine(root,"results.json"),JsonSerializer.Serialize(results));
    Console.WriteLine($"{results.Count-failures}/{results.Count} passed"); return failures==0?0:1;
}
ManagerUninstallTests.Run(root, Test);
TeamAccessTests.Run(Test);
RepositoryOwnershipTests.Run(root, Test);
ReleaseTests.Run(Test, Require, root);
ReleaseUpdateTests.Run(Test, Require, root);
SingleNativeButtonTests.Run(Test, Paths, Require, root);
var v1 = Package("v1", "version one");
var v2 = Package("v2", "version two");
var tool = Catalog.Cad;
Test("InstallAndFingerprint", () =>
{
    var service = new InstallService(Paths("install"));
    service.Install(tool, new(v1, "1"));
    var status = service.Status(tool);
    Require(status.Managed && !status.Modified && status.Version == "1", "Installed state mismatch");
});
Test("UpdateBacksUpAndRestore", () =>
{
    var service = new InstallService(Paths("update"));
    service.Install(tool, new(v1, "1"));
    service.Install(tool, new(v2, "2"));
    Require(service.Status(tool).Version == "2" && service.Backups(tool).Count == 1, "Update backup missing");
    service.Restore(tool, service.Backups(tool).Single());
    Require(service.Status(tool).Version == "1" && !service.Status(tool).Modified, "Restore failed");
});
Test("RemovePreservesRestorableBackup", () =>
{
    var service = new InstallService(Paths("remove"));
    service.Install(tool, new(v1, "1"));
    service.Remove(tool);
    Require(!Directory.Exists(service.Paths.Target(tool)) && !service.ReadState().ContainsKey(tool.Id), "Remove incomplete");
    service.Restore(tool, service.Backups(tool).First());
    Require(service.Status(tool).Managed, "Restore after remove failed");
});
Test("LocalChangesBlockUpdate", () =>
{
    var service = new InstallService(Paths("dirty-update"));
    service.Install(tool, new(v1, "1"));
    File.WriteAllText(Path.Combine(service.Paths.Target(tool), "personal.txt"), "keep");
    Reject(() => service.Install(tool, new(v2, "2")));
    Require(File.ReadAllText(Path.Combine(service.Paths.Target(tool), "personal.txt")) == "keep", "Local change lost");
});
Test("LocalChangesBlockRemoval", () =>
{
    var service = new InstallService(Paths("dirty-remove"));
    service.Install(tool, new(v1, "1"));
    File.WriteAllText(Path.Combine(service.Paths.Target(tool), "PackageContents.xml"), "changed");
    Reject(() => service.Remove(tool));
    Require(Directory.Exists(service.Paths.Target(tool)), "Modified installation removed");
});
Test("UnmanagedFolderNeverOverwritten", () =>
{
    var paths = Paths("external");
    Directory.CreateDirectory(paths.Target(tool));
    File.WriteAllText(Path.Combine(paths.Target(tool), "mine.txt"), "keep");
    var service = new InstallService(paths);
    Reject(() => service.Install(tool, new(v1, "1")));
    Reject(() => service.Remove(tool));
    Require(File.Exists(Path.Combine(paths.Target(tool), "mine.txt")), "External installation changed");
});
Test("RunningHostBlocksInstall", () =>
{
    var paths = Paths("host");
    var service = new InstallService(paths, _ => throw new IOException("host running"));
    Reject(() => service.Install(tool, new(v1, "1")));
    Require(!Directory.Exists(paths.Target(tool)), "Running host was modified");
});
Test("HostRecheckedAfterPreparation", () =>
{
    var paths = Paths("host-race");
    var calls = 0;
    var service = new InstallService(paths, _ => { if (++calls == 2) throw new IOException("host started"); });
    Reject(() => service.Install(tool, new(v1, "1")));
    Require(calls == 2 && !Directory.Exists(paths.Target(tool)), "Host recheck missing");
});
Test("InvalidPackageRejected", () =>
{
    var source = Path.Combine(root, "empty"); Directory.CreateDirectory(source);
    var paths = Paths("invalid");
    Reject(() => new InstallService(paths).Install(tool, new(source, "invalid")));
    Require(!Directory.Exists(paths.Target(tool)), "Invalid package installed");
});
Test("RevitManagedInstallUpdateRemove", () =>
{
    var service = new InstallService(Paths("revit"));
    service.Install(Catalog.Revit, new(Package("r1", "one", Catalog.Revit), "r1"));
    service.Install(Catalog.Revit, new(Package("r2", "two", Catalog.Revit), "r2"));
    Require(service.Status(Catalog.Revit).Version == "r2", "Revit update failed");
    service.Remove(Catalog.Revit);
    Require(!Directory.Exists(service.Paths.Target(Catalog.Revit)), "Revit remove failed");
});
Test("TamperedStateTargetRejected", () =>
{
    var paths = Paths("state-tamper"); var service = new InstallService(paths);
    service.Install(tool, new(v1, "1"));
    var state = service.ReadState();
    state[tool.Id] = state[tool.Id] with { Path = root };
    File.WriteAllText(Path.Combine(paths.Data, "installed.json"), JsonSerializer.Serialize(state));
    Reject(() => service.Remove(tool));
    Require(Directory.Exists(paths.Target(tool)), "Tampered target was removed");
});
Test("TamperedBackupRejected", () =>
{
    var service = new InstallService(Paths("backup-tamper"));
    service.Install(tool, new(v1, "1")); service.Install(tool, new(v2, "2"));
    var backup = service.Backups(tool).First();
    File.WriteAllText(Path.Combine(backup.Directory, "content", "PackageContents.xml"), "tampered");
    Reject(() => service.Restore(tool, backup));
    Require(service.Status(tool).Version == "2", "Tampered backup replaced current");
});
Test("ConcurrentOperationsBlocked", () =>
{
    var paths = Paths("lock"); Directory.CreateDirectory(paths.Data);
    using var locked = new FileStream(Path.Combine(paths.Data, "operation.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    Reject(() => new InstallService(paths).Install(tool, new(v1, "1")));
});
Test("PersistenceFailureRestoresOldFiles", () =>
{
    var paths = Paths("persistence"); var service = new InstallService(paths);
    service.Install(tool, new(v1, "1"));
    var state = Path.Combine(paths.Data, "installed.json"); File.SetAttributes(state, FileAttributes.ReadOnly);
    try { Reject(() => service.Install(tool, new(v2, "2"))); }
    finally { File.SetAttributes(state, FileAttributes.Normal); }
    Require(service.Status(tool).Version == "1" && !service.Status(tool).Modified, "Failed state write lost installation");
});
foreach (var (name, entryName, symlink, duplicate) in new[] {
    ("ZipTraversalRejected", "../escape.txt", false, false),
    ("ZipAbsolutePathRejected", "C:/escape.txt", false, false),
    ("ZipSymlinkRejected", "link", true, false),
    ("ZipDuplicateRejected", "same.txt", false, true) })
{
    Test(name, () =>
    {
        var archive = Path.Combine(root, name + ".zip");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry(entryName);
            if (symlink) entry.ExternalAttributes = 0xA000 << 16;
            using (var writer = new StreamWriter(entry.Open())) writer.Write("test");
            if (duplicate) { using var writer = new StreamWriter(zip.CreateEntry(entryName).Open()); writer.Write("duplicate"); }
        }
        Reject(() => SafeFiles.Extract(archive, Path.Combine(root, name)));
        Require(!File.Exists(Path.Combine(root, "escape.txt")), "Archive escaped extraction root");
    });
}
Test("ValidZipExtracted", () =>
{
    var archive = Path.Combine(root, "valid.zip");
    ZipFile.CreateFromDirectory(v1, archive);
    var dest = Path.Combine(root, "extracted"); SafeFiles.Extract(archive, dest);
    Require(SafeFiles.Same(SafeFiles.Hashes(v1), SafeFiles.Hashes(dest)), "Valid archive changed");
});
Test("DeleteOutsideParentRejected", () => Reject(() => SafeFiles.DeleteOwnedTree(Path.Combine(root, "inside"), Path.Combine(root, "outside"))));
Test("PathTraversalRejected", () => Reject(() => SafeFiles.Child(root, "../elsewhere")));
Test("WindowsAmbiguousPathRejected", () => Reject(() => SafeFiles.Child(root, ".. /elsewhere")));

var profileSource = Package("profile-source", "shared helper", Catalog.Revit);
Directory.CreateDirectory(Path.Combine(profileSource, "dll"));
File.WriteAllText(Path.Combine(profileSource, "dll", "manifest.json"), "{}");
Directory.CreateDirectory(Path.Combine(profileSource, ".git"));
File.WriteAllText(Path.Combine(profileSource, ".git", "config"), "do not deploy git metadata");
foreach (var panel in new[] { "TEST_jm.panel", "TEST_JH.panel" })
{
    var button = Path.Combine(profileSource, "RoBIM TEST.tab", panel, "Sample.pushbutton");
    Directory.CreateDirectory(button);
    File.WriteAllText(Path.Combine(button, "script.py"), "# original dependency-preserving TEST\nprint('test')");
    File.WriteAllBytes(Path.Combine(button, "icon.png"), [1, 2, 3]);
}
Directory.CreateDirectory(Path.Combine(profileSource, "RoBIM TEST.tab", "TEST_empty.panel"));
File.WriteAllText(Path.Combine(profileSource, "bundle.yaml"), "layout:\n  - RoBIM 기본\n  - RoBIM TEST\n");
Test("UserProfileHidesTestLayout", () =>
{
    var before = SafeFiles.Hashes(profileSource);
    var prepared = RevitPackages.Prepare(new(profileSource, "main@123456"), Path.Combine(root, "profile-user"), false);
    Require(!File.ReadAllText(Path.Combine(prepared.Path, "bundle.yaml")).Contains("RoBIM TEST"), "TEST visible in user profile");
    Require(File.Exists(Path.Combine(prepared.Path, "lib", "test.py")) && File.Exists(Path.Combine(prepared.Path, "dll", "manifest.json")), "Dependencies lost");
    Require(!Directory.Exists(Path.Combine(prepared.Path, ".git")), "Runtime snapshot has Git metadata");
    Require(SafeFiles.Same(before, SafeFiles.Hashes(profileSource)), "Source working tree changed");
});
Test("DeveloperSelectedPanelLayoutAndOriginalFiles", () =>
{
    var prepared = RevitPackages.Prepare(new(profileSource, "codex/jm/test@123456"), Path.Combine(root, "profile-developer"), true, ["TEST_jm.panel"]);
    var layout = File.ReadAllText(Path.Combine(prepared.Path, "RoBIM TEST.tab", "bundle.yaml"));
    Require(layout.Contains("TEST_jm") && !layout.Contains("TEST_JH"), "Unselected panel visible");
    Require(File.Exists(Path.Combine(prepared.Path, "RoBIM 기본.tab", "bundle.yaml")) == File.Exists(Path.Combine(profileSource, "RoBIM 기본.tab", "bundle.yaml")), "Dependency tree changed");
    Require(File.ReadAllBytes(Path.Combine(prepared.Path, "RoBIM TEST.tab", "TEST_jm.panel", "Sample.pushbutton", "icon.png")).SequenceEqual(new byte[] {1,2,3}), "Icon lost");
    Require(File.ReadAllText(Path.Combine(prepared.Path, "extension.json")).Contains("\"default_enabled\":false"), "Developer install enabled by default");
});
Test("UnknownTestPanelRejected", () => Reject(() => RevitPackages.Prepare(new(profileSource, "v"), Path.Combine(root, "unknown-panel"), true, ["../outside.panel"])));
Test("EmptyTestPanelRejected", () => Reject(() => RevitPackages.Prepare(new(profileSource, "v"), Path.Combine(root, "empty-panel"), true, ["TEST_empty.panel"])));
Test("NoTestPanelRejected", () => Reject(() => RevitPackages.Prepare(new(profileSource, "v"), Path.Combine(root, "no-panel"), true, [])));
Test("DeveloperInstallationIndependentOfUser", () =>
{
    var service = new InstallService(Paths("profiles-install"));
    service.Install(Catalog.Revit, new(Path.Combine(root, "profile-user"), "user"));
    service.Install(Catalog.RevitDeveloper, new(Path.Combine(root, "profile-developer"), "dev"));
    Require(service.Status(Catalog.Revit).Version == "user" && service.Status(Catalog.RevitDeveloper).Version == "dev", "Profiles overwritten");
});
Test("BranchArgumentsValidated", () =>
{
    RevitPackages.ValidateBranch("codex/jm/my-test");
    foreach (var branch in new[] { "--upload-pack=bad", "../main", "main:secret", "main@{1}", "a b", "a//b" }) Reject(() => RevitPackages.ValidateBranch(branch));
});
Test("ProfileActivationOnlyOneEnabled", () =>
{
    var flags = new Dictionary<string, bool> { ["RoBIM"] = true, ["RoBIM-Developer"] = false };
    var service = new RevitProfiles(Paths("activate"), () => { }, (name, enabled) =>
    { flags[name] = enabled; Require(!flags.Values.All(x => x), "Both profiles enabled"); return Task.CompletedTask; }, name => flags[name]);
    service.Apply(true).GetAwaiter().GetResult();
    Require(!flags["RoBIM"] && flags["RoBIM-Developer"] && service.State()?.Mode == "Developer", "Developer activation failed");
    service.Apply(false).GetAwaiter().GetResult();
    Require(flags["RoBIM"] && !flags["RoBIM-Developer"] && service.State()?.Mode == "User", "User restoration failed");
});
Test("ProfileActivationFailureRestoresFlags", () =>
{
    var flags = new Dictionary<string, bool> { ["RoBIM"] = true, ["RoBIM-Developer"] = false };
    var failed = false;
    var service = new RevitProfiles(Paths("activate-fail"), () => { }, (name, enabled) =>
    { if (name == "RoBIM-Developer" && enabled && !failed) { failed = true; throw new IOException("CLI failed"); } flags[name] = enabled; return Task.CompletedTask; }, name => flags[name]);
    Reject(() => service.Apply(true).GetAwaiter().GetResult());
    Require(flags["RoBIM"] && !flags["RoBIM-Developer"] && service.State() is null, "Failed activation changed original profile");
});
Test("ProfileBlockedWhenHostRunning", () =>
{
    var called = false;
    var service = new RevitProfiles(Paths("activate-host"), () => throw new IOException("host running"), (_, _) => { called = true; return Task.CompletedTask; }, _ => true);
    Reject(() => service.Apply(true).GetAwaiter().GetResult()); Require(!called, "Profile modified while host running");
});
Test("EditableManagementAndFeatureFallback", () =>
{
    var native = new RibbonTool("native:tool:run", "기능", "RoBIM 관리", "", true);
    var tools = RibbonLayouts.WithManagementTools([native, new("robim-revit:legacy", "기존", "", "", true)]);
    Require(tools.Length == 5 && tools[0].Tab == "RoBIM 도구" && tools[0].Panel == "도구", "Missing feature panel fell into Management");
    Require(RibbonLayouts.DefaultLocation(native with { Panel = "독립 Add-in" }).Tab == "RoBIM 도구", "Feature default occupied reserved management tab");
    Require(RibbonLayouts.FromGroup("native:x:run", "X", "RoBIM 관리 / ", true).Panel == "도구", "Empty grouped panel not normalized");
    Require(RibbonLayouts.WithManagementTools([native with { Installed = false }]).Length == 1, "Management appeared without installed tools");
    var user = new RibbonLayout(1, "Revit", [new("사용자", [new("운영", [new("manager:RoBIM.Native.Reload", "다시 적용", "Small", "management", 2), new("manager:RoBIM.Native.Manager", "도구 관리", "Small", "management", 2)])])]);
    var merged = RibbonLayouts.IncludeMissingManagement(user, tools);
    Require(merged.Tabs[0] == user.Tabs[0] && merged.Tabs.SelectMany(t=>t.Panels).SelectMany(p=>p.Buttons).Count(b=>b.Id.StartsWith("manager:")) == 3, "Existing Management layout changed or missing control not added");
    var store = new RibbonLayouts(Paths("management-layout")); store.Save(merged); var reread = store.Read("Revit");
    Require(reread.Tabs[0].Panels[0].Buttons[0].Title == "다시 적용" && reread.Tabs[0].Panels[0].Buttons[1].StackRows == 2, "Management edit did not persist");
    store.EnsureInstalled(native, []);
    Require(store.Read("Revit").Tabs.Any(t=>t.Name == "RoBIM 도구" && t.Panels.Any(p=>p.Name == "도구" && p.Buttons.Any(b=>b.Id == native.Id))), "Auto placement used Management fallback");
});
Test("RibbonLayoutRoundTripAndInstallationIndependence", () =>
{
    var target = Paths("ribbon-config"); var store = new RibbonLayouts(target);
    var before = new InstallService(target).ReadState().Count;
    var layout = new RibbonLayout(1, "Revit", [new("사용자 탭", [new("사용자 패널", [new("robim-revit:button", "표시 이름", "Small")])])]);
    store.Save(layout); var actual = store.Read("Revit");
    Require(actual.Tabs[0].Panels[0].Buttons[0].Size == "Small" && actual.Tabs[0].Name == "사용자 탭", "Layout round trip");
    Require(new InstallService(target).ReadState().Count == before, "Saving layout installed a tool");
    var defaults = RibbonLayouts.Defaults("Revit", [new("installed", "설치", "RoBIM", "도구", true), new("uninstalled", "미설치", "RoBIM", "도구", false)]);
    Require(defaults.Tabs[0].Panels[0].Buttons.Length == 1 && RibbonLayouts.Defaults("Revit", []).Tabs.Length == 0, "Uninstalled tools or empty tabs in defaults");
});
Test("RibbonLayoutRejectsDuplicatesAndInvalidSizesWithoutLosingSavedLayout", () =>
{
    var store = new RibbonLayouts(Paths("ribbon-reject")); var valid = new RibbonLayout(1, "Revit", [new("RoBIM", [new("도구", [new("id", "버튼")])])]); store.Save(valid);
    Reject(() => store.Save(new(1, "Revit", [new("RoBIM", [new("도구", [new("id", "버튼"), new("id", "중복")])])])));
    Reject(() => store.Save(new(1, "Revit", [new("RoBIM", [new("도구", [new("id", "버튼", "Huge")])])])));
    Require(store.Read("Revit").Tabs[0].Panels[0].Buttons.Length == 1, "Invalid write replaced valid layout");
});
Test("RibbonTwoAndThreeRowColumnsRoundTrip", () =>
{
    var store = new RibbonLayouts(Paths("ribbon-stacked"));
    var buttons = new[] { new RibbonPlacement("large", "큰 버튼"), new("a", "A", "Small", "two", 2), new("b", "B", "Small", "two", 2), new("c", "C", "Small", "three", 3), new("d", "D", "Small", "three", 3), new("e", "E", "Small", "three", 3) };
    store.Save(new(1, "Revit", [new("RoBIM", [new("패널", buttons)])]));
    var actual = store.Read("Revit").Tabs[0].Panels[0].Buttons;
    Require(actual.Count(b => b.Stack == "two") == 2 && actual.Count(b => b.Stack == "three") == 3 && actual[0].Stack is null, "Stack membership or column order changed");
});
Test("RibbonColumnsRejectIncompleteMixedAndOversizedGroups", () =>
{
    static RibbonLayout Layout(params RibbonPlacement[] buttons) => new(1, "Revit", [new("RoBIM", [new("패널", buttons)])]);
    Reject(() => RibbonLayouts.Validate(Layout(new RibbonPlacement("a", "A", "Small", "g", 2))));
    Reject(() => RibbonLayouts.Validate(Layout(new("a", "A", "Small", "g", 2), new("b", "B", "Small", "g", 3))));
    Reject(() => RibbonLayouts.Validate(Layout(new("a", "A", "Small", "g", 2), new("b", "B", "Small", "g", 2), new("c", "C", "Small", "g", 2))));
    Reject(() => RibbonLayouts.Validate(Layout(new("a", "A", "Large", "g", 2), new("b", "B", "Small", "g", 2))));
    Reject(() => RibbonLayouts.Validate(Layout(new RibbonPlacement("a", "A", "Small", null, 2))));
});
Test("RibbonColumnCannotSpanPanels", () =>
{
    Reject(() => RibbonLayouts.Validate(new(1, "Revit", [new("RoBIM", [new("P1", [new("a", "A", "Small", "g", 2), new("b", "B", "Small", "g", 2)]), new("P2", [new("c", "C", "Small", "g", 2), new("d", "D", "Small", "g", 2)])])])));
});
Test("RibbonSeparatorsRoundTripAndRejectStackSplits", () =>
{
    var store = new RibbonLayouts(Paths("ribbon-separators"));
    var separator = new RibbonPlacement("separator:one", "구분선", Kind: "Separator");
    RibbonLayout Layout(params RibbonPlacement[] items) => new(1, "Revit", [new("RoBIM", [new("패널", items)])]);
    store.Save(Layout(new("a", "첫째"), separator, new("b", "둘째")));
    Require(store.Read("Revit").Tabs[0].Panels[0].Buttons[1].Kind == "Separator", "Separator order or type lost");
    Reject(() => RibbonLayouts.Validate(Layout(separator with { Stack = "g", Size = "Small", StackRows = 2 })));
    Reject(() => RibbonLayouts.Validate(Layout(separator with { Kind = "Unknown" })));
    Reject(() => RibbonLayouts.Validate(Layout(new("a", "A", "Small", "g", 2), separator, new("b", "B", "Small", "g", 2))));
    Require(store.Read("Revit").Tabs[0].Panels[0].Buttons.Length == 3, "Invalid separator write lost layout");
});
Test("UserDataDirectoryPhysicalPathIsVerifiedAndProbeRemoved", () =>
{
    var directory = Paths("physical-path-check").Data;
    Require(WindowsDataPath.EnsureDirectory(directory).Equals(Path.GetFullPath(directory), StringComparison.OrdinalIgnoreCase), "Wrong physical data directory");
    Require(!Directory.EnumerateFiles(directory).Any(), "Physical path probe was left behind");
});
Test("VirtualizedDataPathRejectedAndExtendedPrefixAccepted", () =>
{
    var logical = Path.Combine(root, "logical", "file");
    WindowsDataPath.VerifyFileLocation(logical, @"\\?\" + logical);
    Reject(() => WindowsDataPath.VerifyFileLocation(logical, Path.Combine(root, "LocalCache", "file")));
});
Test("OnlyActivationFlagReadFromIni", () =>
{
    var ini = Path.Combine(root, "profile-fixture.ini");
    File.WriteAllText(ini, "[other]\npassword=not-a-real-credential\n[RoBIM.extension]\ndisabled=false\n[RoBIM-Developer.extension]\ndisabled=true\n");
    Require(RevitProfiles.ReadEnabled(ini, "RoBIM") && !RevitProfiles.ReadEnabled(ini, "RoBIM-Developer"), "INI flag parse incorrect");
});
Test("CopiedProfileIniActivationPreservesOtherSettings", () =>
{
    var ini = Path.Combine(root, "copied-profile.ini");
    var other = "[other]\r\npassword=fixture-only\r\n";
    File.WriteAllText(ini, other + "[RoBIM.extension]\r\ndisabled=false\r\n[tail]\r\nvalue=keep\r\n");
    RevitProfiles.WriteEnabled(ini, "RoBIM", false);
    RevitProfiles.WriteEnabled(ini, "RoBIM-Selected", true);
    Require(!RevitProfiles.ReadEnabled(ini, "RoBIM") && RevitProfiles.ReadEnabled(ini, "RoBIM-Selected"), "Copied profile activation flags");
    Require(File.ReadAllText(ini).Contains(other) && File.ReadAllText(ini).Contains("[tail]\r\nvalue=keep"), "Unrelated settings changed");
    var before = File.ReadAllText(ini);
    Reject(() => RevitProfiles.WriteEnabled(ini, "other", true));
    Require(File.ReadAllText(ini) == before, "Unsupported profile modified config");
});
Test("CopiedProfileIniPreservesBomAndRejectsAmbiguousFlags", () =>
{
    var ini = Path.Combine(root, "copied-profile-bom.ini");
    File.WriteAllText(ini, "[RoBIM-Selected.extension]\r\ndisabled = true\r\n", new System.Text.UTF8Encoding(true));
    RevitProfiles.WriteEnabled(ini, "RoBIM-Selected", true);
    Require(RevitProfiles.ReadEnabled(ini, "RoBIM-Selected") && File.ReadAllBytes(ini)[0] == 0xef, "BOM profile activation failed");
    File.WriteAllText(ini, "[RoBIM.extension]\ndisabled=true\ndisabled=false\n");
    var before = File.ReadAllBytes(ini);
    Reject(() => RevitProfiles.WriteEnabled(ini, "RoBIM", true));
    Require(before.SequenceEqual(File.ReadAllBytes(ini)), "Ambiguous config was modified");
});

Test("ReloadPrepareDoesNotChangeInstalledFiles", () =>
{
    var paths = Paths("reload-prepare"); var service = new InstallService(paths);
    service.Install(Catalog.Revit, new(profileSource, "old"));
    var next = Package("reload-next", "updated Python", Catalog.Revit);
    new RevitReload(paths).Prepare(Catalog.Revit, new(next, "next"));
    Require(service.Status(Catalog.Revit).Version == "old" && !service.Status(Catalog.Revit).Modified, "Prepare changed live extension");
    new RevitReload(paths).Apply(() => { }).GetAwaiter().GetResult();
    Require(service.Status(Catalog.Revit).Version == "next" && service.Backups(Catalog.Revit).Count == 1, "Live apply or backup failed");
});
Test("ReloadFirstScriptInstallAllowed", () =>
{
    var paths = Paths("reload-first"); var reload = new RevitReload(paths);
    reload.Prepare(Catalog.RevitDeveloper, new(profileSource, "first"));
    reload.Apply(() => { }).GetAwaiter().GetResult();
    Require(new InstallService(paths).Status(Catalog.RevitDeveloper).Managed, "First script installation failed");
});
Test("ReloadBinaryChangeRequiresRestart", () =>
{
    var paths = Paths("reload-binary"); var next = Package("reload-dll", "helper", Catalog.Revit);
    File.WriteAllText(Path.Combine(next, "lib", "locked.dll"), "binary");
    var reload = new RevitReload(paths); reload.Prepare(Catalog.Revit, new(next, "binary"));
    Reject(() => reload.Apply(() => { }).GetAwaiter().GetResult());
    Require(!Directory.Exists(paths.Target(Catalog.Revit)) && File.Exists(Path.Combine(paths.Data, "reload-pending.json")), "Restart-only package changed live files or was lost");
});
Test("ReloadUnchangedBinaryAllowedButRemovalRejected", () =>
{
    RevitReload.EnsureReloadable(new() { ["lib/core.dll"] = "same", ["script.py"] = "old" }, new() { ["lib/core.dll"] = "same", ["script.py"] = "new" });
    Reject(() => RevitReload.EnsureReloadable(new() { ["lib/core.dll"] = "same" }, new()));
    Reject(() => RevitReload.EnsureReloadable(new(), new() { ["script.cs"] = "new" }));
});
Test("ReloadTamperedPreparedFilesRejected", () =>
{
    var paths = Paths("reload-tamper"); var reload = new RevitReload(paths);
    reload.Prepare(Catalog.Revit, new(profileSource, "prepared"));
    var manifest = JsonSerializer.Deserialize<ReloadPackage>(File.ReadAllText(Path.Combine(paths.Data, "reload-pending.json")))!;
    File.WriteAllText(Path.Combine(manifest.Source, "lib", "test.py"), "tampered");
    Reject(() => reload.Apply(() => { }).GetAwaiter().GetResult());
    Require(!Directory.Exists(paths.Target(Catalog.Revit)), "Tampered package installed");
});
Test("ReloadStaleInstallationPreserved", () =>
{
    var paths = Paths("reload-stale"); var service = new InstallService(paths);
    service.Install(Catalog.Revit, new(profileSource, "old"));
    var reload = new RevitReload(paths); reload.Prepare(Catalog.Revit, new(profileSource, "next"));
    File.WriteAllText(Path.Combine(paths.Target(Catalog.Revit), "lib", "test.py"), "personal change");
    Reject(() => reload.Apply(() => { }).GetAwaiter().GetResult());
    Require(File.ReadAllText(Path.Combine(paths.Target(Catalog.Revit), "lib", "test.py")) == "personal change", "Local edits lost");
});
Test("ReloadInvalidSessionBlocksBeforeMutation", () =>
{
    var paths = Paths("reload-session"); var reload = new RevitReload(paths);
    reload.Prepare(Catalog.Revit, new(profileSource, "next"));
    Reject(() => reload.Apply(() => throw new IOException("Wrong Revit session")).GetAwaiter().GetResult());
    Require(!Directory.Exists(paths.Target(Catalog.Revit)), "Invalid session installed files");
    Reject(() => RevitReload.EnsureSession(-1, 0));
});
Test("ReloadSessionRecheckedAfterCopy", () =>
{
    var paths = Paths("reload-session-race"); var reload = new RevitReload(paths);
    reload.Prepare(Catalog.Revit, new(profileSource, "next")); var calls = 0;
    Reject(() => reload.Apply(() => { if (++calls >= 4) throw new IOException("Another Revit opened"); }).GetAwaiter().GetResult());
    Require(calls == 4 && !Directory.Exists(paths.Target(Catalog.Revit)), "Session not rechecked before replacement");
});
Test("ReloadOnlyRevitPackagesAndOwnedPaths", () =>
{
    var paths = Paths("reload-path"); var reload = new RevitReload(paths);
    Reject(() => reload.Prepare(Catalog.Cad, new(v1, "cad")));
    reload.Prepare(Catalog.Revit, new(profileSource, "next"));
    var file = Path.Combine(paths.Data, "reload-pending.json");
    var manifest = JsonSerializer.Deserialize<ReloadPackage>(File.ReadAllText(file))!;
    File.WriteAllText(file, JsonSerializer.Serialize(manifest with { Source = profileSource }));
    Reject(() => reload.Apply(() => { }).GetAwaiter().GetResult());
});
Test("ReloadProfilePreparationAndRetry", () =>
{
    var paths = Paths("reload-profile"); var reload = new RevitReload(paths); var called = false;
    reload.PrepareProfile(true); Require(!called, "Prepare activated profile");
    Reject(() => reload.Apply(() => { }, _ => throw new IOException("Profile failed")).GetAwaiter().GetResult());
    Require(File.Exists(Path.Combine(paths.Data, "reload-profile.json")), "Failed request lost");
    reload.Apply(() => { }, developer => { called = developer; return Task.CompletedTask; }).GetAwaiter().GetResult();
    Require(called && !File.Exists(Path.Combine(paths.Data, "reload-profile.json")), "Profile apply not consumed");
});

Test("ReloadUpdatesWithRootAndUnchangedBinaryLocked", () =>
{
    var paths = Paths("reload-locked-root"); var service = new InstallService(paths);
    var source = Package("reload-locked-v1", "before", Catalog.Revit);
    File.WriteAllText(Path.Combine(source, "lib", "same.dll"), "identical binary");
    service.Install(Catalog.Revit, new(source, "1"));
    var next = Package("reload-locked-v2", "after", Catalog.Revit);
    File.WriteAllText(Path.Combine(next, "lib", "same.dll"), "identical binary");
    var target = paths.Target(Catalog.Revit); var cwd = Environment.CurrentDirectory;
    using var locked = new FileStream(Path.Combine(target, "lib", "same.dll"), FileMode.Open, FileAccess.Read, FileShare.Read);
    try
    {
        Environment.CurrentDirectory = target;
        service.InstallReload(Catalog.Revit, new(next, "2"));
        Require(service.Status(Catalog.Revit) is { Modified: false, Version: "2" }, "Locked-root update failed");
        Require(File.ReadAllText(Path.Combine(target, "lib", "test.py")) == "after", "Script update missing");
    }
    finally { Environment.CurrentDirectory = cwd; }
});
Test("ReloadDeltaRemovesCommandFilesAndPreservesRootStructure", () =>
{
    var paths = Paths("reload-delta-remove"); var service = new InstallService(paths);
    var source = Package("reload-delta-v1", "shared", Catalog.Revit);
    var command = Path.Combine(source, "RoBIM 기본.tab", "Tools.panel", "Example.pushbutton");
    Directory.CreateDirectory(command); File.WriteAllText(Path.Combine(command, "script.py"), "pass");
    service.Install(Catalog.Revit, new(source, "1"));
    var next = Package("reload-delta-v2", "shared", Catalog.Revit);
    service.InstallReload(Catalog.Revit, new(next, "2"));
    Require(!Directory.Exists(Path.Combine(paths.Target(Catalog.Revit), "RoBIM 기본.tab", "Tools.panel", "Example.pushbutton")), "Removed command folder retained");
    InstallService.ValidatePackage(Catalog.Revit, paths.Target(Catalog.Revit));
    Require(!service.Status(Catalog.Revit).Modified && service.Backups(Catalog.Revit).Count == 1, "Delta state or backup mismatch");
});
Test("ReloadDeltaRejectsBinaryChangeBeforeWriting", () =>
{
    var paths = Paths("reload-delta-binary"); var service = new InstallService(paths);
    var source = Package("reload-binary-v1", "before", Catalog.Revit);
    File.WriteAllText(Path.Combine(source, "lib", "same.dll"), "old"); service.Install(Catalog.Revit, new(source, "1"));
    var next = Package("reload-binary-v2", "after", Catalog.Revit);
    File.WriteAllText(Path.Combine(next, "lib", "same.dll"), "new");
    Reject(() => service.InstallReload(Catalog.Revit, new(next, "2")));
    Require(service.Status(Catalog.Revit) is { Modified: false, Version: "1" }, "Rejected binary update mutated files");
});
Test("ReloadDeltaRestoresEarlierFilesAfterLockedChange", () =>
{
    var paths = Paths("reload-delta-rollback"); var service = new InstallService(paths);
    var source = Package("reload-rollback-v1", "before", Catalog.Revit);
    File.WriteAllText(Path.Combine(source, "lib", "z.txt"), "locked before"); service.Install(Catalog.Revit, new(source, "1"));
    var next = Package("reload-rollback-v2", "after", Catalog.Revit);
    File.WriteAllText(Path.Combine(next, "lib", "z.txt"), "locked after");
    using var locked = new FileStream(Path.Combine(paths.Target(Catalog.Revit), "lib", "z.txt"), FileMode.Open, FileAccess.Read, FileShare.Read);
    Reject(() => service.InstallReload(Catalog.Revit, new(next, "2")));
    Require(service.Status(Catalog.Revit) is { Modified: false, Version: "1" }, "Partial delta was not restored");
});

Test("GitUrlNormalization", () =>
{
    Require(ExtensionCatalog.Repository("https://github.com/owner/tools.git") == "owner/tools", "HTTPS URL normalization failed");
    Require(ExtensionCatalog.Repository("git@github.com:owner/tools.git") == "owner/tools", "SSH style normalization failed");
});
Test("GitUrlRejectsCredentialsAndForeignHosts", () =>
{
    foreach (var url in new[] { "https://user:secret@github.com/a/b", "file:///C:/repo", "https://example.com/a/b", "https://github.com/a/b?token=secret", "https://github.com/a/b/tree/main", "https://github.com/a/../b", "https://github.com/a/b#main" })
        Reject(() => ExtensionCatalog.Repository(url));
});
string Manifest(params ExtensionPackage[] packages) => JsonSerializer.Serialize(new ExtensionManifest(1, packages));
var customEntry = new ExtensionPackage("sample", "Sample Tools", "pyrevit", ".", "Sample.extension");
Test("ExplicitAddinTopicFilter", () =>
{
    Require(ExtensionCatalog.Related(["robim-addon"]) && ExtensionCatalog.Related(["autocad-plugin"]), "Addon marker omitted");
    Require(!ExtensionCatalog.Related(["python", "autocad", "revit", "web"]), "General repository included");
});
Test("PackageManifestSchemaAndPathValidation", () =>
{
    Require(ExtensionCatalog.ParseManifest(Manifest(customEntry)).Single() == customEntry, "Manifest roundtrip failed");
    foreach (var entry in new[] { customEntry with { Path = "../outside" }, customEntry with { Path = "C:/outside" }, customEntry with { Path = "" }, customEntry with { Folder = "../outside.extension" }, customEntry with { Host = "revit-addin" }, customEntry with { Distribution = "script" }, customEntry with { Distribution = "release", Asset = "../package.zip" } })
        Reject(() => ExtensionCatalog.ParseManifest(Manifest(entry)));
    Reject(() => ExtensionCatalog.ParseManifest("{\"schemaVersion\":2,\"packages\":[]}"));
});
Test("PackageManifestDuplicateIdentityBlocked", () =>
{
    Reject(() => ExtensionCatalog.ParseManifest(Manifest(customEntry, customEntry)));
    Reject(() => ExtensionCatalog.ParseManifest(Manifest(customEntry, customEntry with { Id = "other" })));
});
Test("PackageRegistrationAndStableIdentity", () =>
{
    var paths = Paths("extension-registry"); var catalog = new ExtensionCatalog(paths);
    var repo = new ExtensionRepository("owner/tools", "main", [customEntry], "manifest");
    catalog.Register(repo); catalog.Register(repo);
    Require(catalog.Registered().Count(x => x.Repository == "owner/tools") == 1 && catalog.Registered().Select(x => x.Repository).Distinct(StringComparer.OrdinalIgnoreCase).Count() == catalog.Registered().Length, "Duplicate registered repository");
    var first = ExtensionCatalog.Tool(repo, customEntry);
    var next = ExtensionCatalog.Tool(repo with { Repository = "OWNER/TOOLS", Branch = "develop" }, customEntry);
    Require(first.Id == next.Id && first.CustomPackage && first.FolderName == "Sample.extension", "Install identity changed with branch/case");
});
Test("ReservedInstallFoldersCannotBeClaimed", () =>
{
    var repo = new ExtensionRepository("owner/tools", "main", [customEntry], "manifest");
    foreach (var folder in new[] { Catalog.Revit.FolderName, Catalog.RevitDeveloper.FolderName, Catalog.ReloadBridge.FolderName, Catalog.Cad.FolderName })
        Reject(() => ExtensionCatalog.Tool(repo, customEntry with { Folder = folder }));
});
Test("GenericPyRevitExtensionWithoutLibInstallRestoreRemove", () =>
{
    var source = Path.Combine(root, "generic-extension"); Directory.CreateDirectory(Path.Combine(source, "Sample.tab"));
    File.WriteAllText(Path.Combine(source, "extension.json"), "{}");
    var repo = new ExtensionRepository("owner/tools", "main", [customEntry], "manifest");
    var tool = ExtensionCatalog.Tool(repo, customEntry); var service = new InstallService(Paths("generic-pyrevit"));
    service.Install(tool, new(source, "1")); service.Install(tool, new(source, "2"));
    service.Restore(tool, service.Backups(tool).First());
    Require(service.Status(tool).Version == "1", "Generic extension restore failed");
    service.Remove(tool); Require(!Directory.Exists(service.Paths.Target(tool)), "Generic extension removal failed");
});
Test("GenericAutoCadModulePathsValidated", () =>
{
    var source = Package("generic-cad", "binary");
    File.WriteAllText(Path.Combine(source, "PackageContents.xml"), "<ApplicationPackage><Components><ComponentEntry ModuleName=\"./Contents/Windows/DelHiddenNet.dll\" /></Components></ApplicationPackage>");
    var repo = new ExtensionRepository("owner/cad", "main", [], "manifest");
    var entry = new ExtensionPackage("cad", "CAD Tools", "autocad", ".", "CustomCad.bundle");
    var tool = ExtensionCatalog.Tool(repo, entry);
    InstallService.ValidatePackage(tool, source);
    File.WriteAllText(Path.Combine(source, "PackageContents.xml"), "<ApplicationPackage><Components><ComponentEntry ModuleName=\"../outside.dll\" /></Components></ApplicationPackage>");
    Reject(() => InstallService.ValidatePackage(tool, source));
});
Test("InvalidTokenCharactersRejectedWithoutEcho", () =>
{
    foreach (var token in new[] { "sensitive-example\r\nvalue", "sensitive-example\0value", "sensitive-example 한글" })
    {
        try { GitHubSession.Request("/user", token: token).GetAwaiter().GetResult(); throw new Exception("Invalid header token accepted"); }
        catch (IOException ex) { Require(!ex.Message.Contains("sensitive-example"), "Token leaked in validation error"); }
    }
});
Test("GitHubApiCredentialsCannotGoToArbitraryHost", () =>
{
    foreach (var path in new[] { "https://example.com/user", "//example.com/user", "/../user", "/\\example.com" })
        Reject(() => GitHubSession.Request(path).GetAwaiter().GetResult());
});

Test("NormalInstallClearsOnlyItsStaleReloadRequest", () =>
{
    var paths = Paths("reload-discard"); var reload = new RevitReload(paths);
    reload.Prepare(Catalog.Revit, new(profileSource, "prepared"));
    reload.PrepareProfile(true);
    reload.DiscardPendingFor(Catalog.RevitDeveloper.Id);
    Require(File.Exists(Path.Combine(paths.Data, "reload-pending.json")), "Another tool request was discarded");
    reload.DiscardPendingFor(Catalog.Revit.Id);
    Require(!File.Exists(Path.Combine(paths.Data, "reload-pending.json")) && File.Exists(Path.Combine(paths.Data, "reload-profile.json")), "Own stale request not cleared or profile lost");
});
Test("SimpleCadFirstInstallNeedsNoLogin", () =>
{
    var status = new ToolStatus(Catalog.Cad, "test", "—", false, false, false, "");
    Require(InstallFlow.Decide(status, false, false, false).Action == InstallAction.BundledCad, "Fresh CAD cannot use bundled package");
    Require(InstallFlow.Decide(status with { Managed = true }, false, false, false).Action == InstallAction.Connect, "Installed CAD silently downgraded to bundle");
});
Test("SimpleInstallRequiresClosedHostForFirstInstall", () =>
{
    foreach (var tool in new[] { Catalog.Cad, Catalog.Revit })
    {
        var step = InstallFlow.Decide(new(tool, "test", "—", false, false, false, ""), true, true, false);
        Require(step.Action == InstallAction.WaitForHost && !step.Enabled, "First install attempted in running host");
    }
});
Test("SimpleRevitUpdatePreparesThenApplies", () =>
{
    var status = new ToolStatus(Catalog.Revit, "test", "dev@123", false, true, false, "");
    Require(InstallFlow.Decide(status, true, true, false).Action == InstallAction.PrepareRevit, "Running Revit update not prepared");
    Require(InstallFlow.Decide(status, true, true, true).Action == InstallAction.ApplyInRevit, "Prepared update lacks apply guidance");
    Require(InstallFlow.Decide(status, false, true, false).Action == InstallAction.Install, "Closed Revit cannot update preserved branch");
});
Test("SimpleInstallPreservesModifiedAndExternalCad", () =>
{
    var status = new ToolStatus(Catalog.Cad, "test", "old", true, false, true, "");
    Require(!InstallFlow.Decide(status, false, true, false).Enabled, "Modified installation can be overwritten");
    Require(InstallFlow.Decide(status with { Modified = false, External = true }, false, true, false).Action == InstallAction.Blocked, "External CAD can be overwritten");
});
Test("SimpleCustomRevitRequiresClosure", () =>
{
    var tool = new ToolSpec("custom", "Example", ToolKind.PyRevit, "owner/repo", "main", "Example.extension", "", true);
    Require(InstallFlow.Decide(new(tool, "test", "1", true, false, false, ""), true, true, false).Action == InstallAction.WaitForHost, "Generic extension used RoBIM reload path");
});

CadReloadTests.Run(root, Test);
if (args.Contains("--online"))
{
    try
    {
        var paths = Paths("online");
        var identity = await GitHubSession.Identify();
        Require(identity.Login.Length > 0, "Authenticated GitHub ID missing");
        results.Add(new { Name = "AuthenticatedGitHubIdentity", Passed = true, Error = "" });
        var registry = new ExtensionCatalog(paths);
        var discovered = await registry.Discover();
        Require(discovered.Repositories.Any(x => x.Repository.Equals(Catalog.Cad.Repository, StringComparison.OrdinalIgnoreCase)) && discovered.Repositories.Any(x => x.Repository.Equals(Catalog.Revit.Repository, StringComparison.OrdinalIgnoreCase)), "RoBIM addin repositories missing");
        results.Add(new { Name = "AuthenticatedAddinRepositoryDiscovery", Passed = true, Error = "" });
        var cadRepo = await registry.Inspect("https://github.com/" + Catalog.Cad.Repository + ".git");
        var genericCad = await registry.Download(cadRepo, cadRepo.Packages.Single());
        Require(File.Exists(Path.Combine(genericCad.Path, "PackageContents.xml")), "Generic release download failed");
        results.Add(new { Name = "GitUrlInspectionAndVerifiedReleaseDownload", Passed = true, Error = "" });
        var credentialTestName = "rt-" + Guid.NewGuid().ToString("N");
        var fakeToken = "test-only-" + Guid.NewGuid().ToString("N");
        try { WindowsTokenStore.Save(credentialTestName, fakeToken); Require(WindowsTokenStore.Get(credentialTestName) == fakeToken, "Windows token roundtrip failed"); }
        finally { WindowsTokenStore.Remove(credentialTestName); fakeToken = ""; }
        results.Add(new { Name = "WindowsCredentialRoundtripAndCleanup", Passed = true, Error = "" });
        var previousAuth = GitHubSession.Current();
        var invalidRejected = false;
        try { await GitHubSession.UseToken("not-a-real-github-token"); } catch (IOException) { invalidRejected = true; }
        Require(invalidRejected && GitHubSession.Current() == previousAuth, "Invalid token changed active authentication");
        results.Add(new { Name = "InvalidTokenPreservesCurrentConnection", Passed = true, Error = "" });
        await Sources.CheckConnection();
        var sources = new Sources(paths);
        var accounts = await GitHubAccount.SavedAccounts();
        Require(accounts.Count > 0, "Saved GitHub login not detected");
        var branches = await sources.RevitBranches();
        Require(branches.Contains("main") && branches.Any(x => x.StartsWith("codex/")), "Developer branches not discovered");
        var cad = await sources.DownloadCad();
        var revit = await sources.DownloadRevit();
        var service = new InstallService(paths);
        service.Install(Catalog.Cad, cad);
        service.Install(Catalog.Revit, revit);
        Require(service.Status(Catalog.Cad).Managed && service.Status(Catalog.Revit).Managed, "Online packages failed to install in test paths");
        var external = await EnvironmentProbe.ExternalRevit(paths.Target(Catalog.Revit));
        Console.WriteLine("Detected existing Revit extension: " + (external ?? "none"));
        results.Add(new { Name = "PrivateGitHubCadAndRevitDownloadInstall", Passed = true, Error = "" });
        // All Git mutations below affect only this newly-created test clone.
        var fixture = paths.Target(Catalog.Revit);
        await ProcessRunner.GitRequired(fixture, "fetch", "--deepen=1", "origin", "main");
        await ProcessRunner.GitRequired(fixture, "checkout", "--detach", "HEAD~1");
        await ProcessRunner.GitRequired(fixture, "branch", "-f", "main", "HEAD");
        await ProcessRunner.GitRequired(fixture, "checkout", "main");
        var oldCommit = await ProcessRunner.GitRequired(fixture, "rev-parse", "HEAD");
        var pinRejected = false;
        try { await EnvironmentProbe.UpdateExternal(fixture, paths, () => { }, oldCommit, new string('0', 40)); }
        catch (IOException ex) when (ex.Message.Contains("원격 커밋")) { pinRejected = true; }
        Require(pinRejected && await ProcessRunner.GitRequired(fixture, "rev-parse", "HEAD") == oldCommit, "Unprepared Git commit applied");
        results.Add(new { Name = "ExternalReloadPinnedCommitRejected", Passed = true, Error = "" });
        await EnvironmentProbe.UpdateExternal(fixture, paths, () => { });
        var newCommit = await ProcessRunner.GitRequired(fixture, "rev-parse", "HEAD");
        Require(oldCommit != newCommit && await ProcessRunner.GitRequired(fixture, "branch", "--show-current") == "main", "External update did not preserve branch and fast-forward");
        Require(Directory.GetFiles(Path.Combine(paths.Data, "ExternalBackups"), "previous-commit.txt", SearchOption.AllDirectories).Length == 1, "External backup missing");
        results.Add(new { Name = "ExternalRevitFastForwardAndBackup", Passed = true, Error = "" });
        File.WriteAllText(Path.Combine(fixture, "manager-personal-test.txt"), "preserve");
        var blocked = false;
        try { await EnvironmentProbe.UpdateExternal(fixture, paths, () => { }); }
        catch (IOException ex) when (ex.Message.Contains("로컬 변경")) { blocked = true; }
        Require(blocked && File.ReadAllText(Path.Combine(fixture, "manager-personal-test.txt")) == "preserve", "Dirty external update was not blocked");
        results.Add(new { Name = "ExternalRevitLocalChangesPreserved", Passed = true, Error = "" });
    }
    catch (Exception ex) { failures++; results.Add(new { Name = "PrivateGitHubCadAndRevitDownloadInstall", Passed = false, Error = ex.ToString() }); Console.WriteLine(ex.Message); }
}
RepositoryDiscoveryTests.Run(root, Test);
ReloadBridgeTests.Run(root, Test);
PyRevitRetirementTests.Run(root, Test);
PyRevitLocationTests.Run(Test, Paths, Require, root);
NativeReleasePolicyTests.Run(Test, Require);
NativeRevitTests.Run(Test, Paths, Require, root);
ButtonTests.Run(Test, Paths, Require, root);
File.WriteAllText(Path.Combine(root, "results.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"{results.Count - failures}/{results.Count} passed. Report: {Path.Combine(root, "results.json")}");
return failures == 0 ? 0 : 1;
