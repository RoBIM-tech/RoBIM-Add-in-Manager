using RoBIM.Manager.Core;
using System.IO;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception("Native-only policy: " + message);
}
var root = Path.Combine(Path.GetTempPath(), "robim-native-policy-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var paths = new ManagerPaths(root);
    Check(RevitRuntimePolicy.NativeOnly(paths), "missing legacy mode file must be NativeOnly");
    File.WriteAllText(Path.Combine(root, "revit-mode.json"), "not-valid-json");
    Check(RevitRuntimePolicy.NativeOnly(paths), "corrupt old mode must not crash");
    foreach (var tool in new[] { Catalog.Revit, Catalog.RevitDeveloper, Catalog.RevitSelected, Catalog.ReloadBridge,
        new ToolSpec("other", "example/legacy", "Other.extension", ToolKind.PyRevit) })
    {
        var rejected = false;
        try { RevitRuntimePolicy.EnsureAllowed(paths, tool); } catch (IOException) { rejected = true; }
        Check(rejected, "legacy tool must be rejected: " + tool.Id);
    }
    foreach (var tool in new[] { new ToolSpec("cad", "example/cad", "CAD.bundle", ToolKind.AutoCAD),
        new ToolSpec("room-check", "example/native", "Room.bundle", ToolKind.NativeRevit) })
        RevitRuntimePolicy.EnsureAllowed(paths, tool);
    Console.WriteLine("PASS Native-only policy: missing/corrupt files, RoBIM and foreign pyRevit rejected; Native and CAD allowed.");
}
finally { Directory.Delete(root, true); }

namespace RoBIM.Manager.Core
{
    public sealed record ManagerPaths(string Data);
    public enum ToolKind { PyRevit, AutoCAD, NativeRevit }
    public sealed record ToolSpec(string Id, string Repository, string FolderName, ToolKind Kind);
    public static class Catalog
    {
        public static readonly ToolSpec Revit = new("revit", "RoBIM-tech/RoBIM-pyrevit", "RoBIM.extension", ToolKind.PyRevit);
        public static readonly ToolSpec RevitDeveloper = new("revit-dev", Revit.Repository, "RoBIM-Developer.extension", ToolKind.PyRevit);
        public static readonly ToolSpec RevitSelected = new("revit-selected", Revit.Repository, "RoBIM-Selected.extension", ToolKind.PyRevit);
        public static readonly ToolSpec ReloadBridge = new("revit-bridge", Revit.Repository, "RoBIM Manager.extension", ToolKind.PyRevit);
    }
}
