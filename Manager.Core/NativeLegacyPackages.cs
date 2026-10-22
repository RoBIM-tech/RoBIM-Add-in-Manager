namespace RoBIM.Manager.Core;

public sealed class NativeLegacyPackages(ManagerPaths paths)
{
    // Fingerprint of the previously distributed RoBIM Sheets v0.1 package.
    private const string SheetsV1 = "D4D57E31B099476E7EEF203FC3A72EFEDF2BE0AA7879A1F5912F481D923F0852";
    public bool Present(ToolSpec tool) => tool.Kind == ToolKind.NativeRevit && Directory.Exists(paths.Target(tool));
    public SourcePackage? VerifiedReference(ToolSpec tool)
    {
        if (tool.Kind != ToolKind.NativeRevit || tool.FolderName != "RoBIM.Revit.Sheets.bundle") return null;
        var downloads = SafeFiles.Child(paths.Data, "Extensions"); if (!Directory.Exists(downloads)) return null;
        foreach (var directory in Directory.GetDirectories(downloads))
        {
            var archive = SafeFiles.Child(directory, "package.zip");
            if (!File.Exists(archive) || !SafeFiles.Hash(archive).Equals(SheetsV1, StringComparison.OrdinalIgnoreCase)) continue;
            var output = SafeFiles.Child(paths.Data, "NativeRevit/LegacyReferences/" + Guid.NewGuid().ToString("N"));
            SafeFiles.Extract(archive, output);
            var reference = SafeFiles.Child(output, tool.FolderName); InstallService.ValidatePackage(tool, reference);
            return new(reference, "0.1.0 verified legacy");
        }
        return null;
    }
}
