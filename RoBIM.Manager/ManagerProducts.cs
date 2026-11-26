using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using RoBIM.Manager.Core;

namespace RoBIM.Manager;

public sealed partial class ManagerBackend
{
    private readonly List<ProductRow> productRows = [];
    private string? selectedProductId;
    private readonly List<PackageRow> availablePackages = [];
    private bool authenticated;
    private string connectedLogin = "";
    private readonly DispatcherTimer localPollTimer = new() { Interval = TimeSpan.FromMilliseconds(1500) };
    internal sealed record ProductRow(ToolStatus Status, PackageRow? Package);

    private void InitializeProductSelection()
    {
        localPollTimer.Tick += async (_, _) => await PollLocalChanges();
        if (!preview) localPollTimer.Start();
    }

    private void DisposeProductPolling() => localPollTimer.Stop();

    private void SelectProduct(string id) => selectedProductId = id;
    private ProductRow? SelectedProduct() => productRows.FirstOrDefault(x => x.Status.Tool.Id == selectedProductId) ?? productRows.FirstOrDefault();

    private static bool HostRunning(ToolKind kind)
    {
        foreach (var name in kind == ToolKind.AutoCAD ? new[] { "acad", "accoreconsole" } : new[] { "Revit" })
        {
            var processes = Process.GetProcessesByName(name);
            try { if (processes.Length > 0) return true; } finally { foreach (var process in processes) process.Dispose(); }
        }
        return false;
    }

    private bool PendingFor(string id)
    {
        try
        {
            var file = SafeFiles.Child(paths.Data, "reload-pending.json");
            return File.Exists(file) && JsonSerializer.Deserialize<ReloadPackage>(File.ReadAllText(file))?.ToolId == id;
        }
        catch (Exception) { return false; }
    }

    private ProductRow[] BuildProductRows(ToolStatus[] statusRows, PackageRow[] packages)
    {
        var result = new List<ProductRow>();
        void Add(ToolStatus status, PackageRow? package)
        {
            if (status.Tool.Kind == ToolKind.PyRevit || RevitRuntimePolicy.LegacyRoBIM(status.Tool)) return;
            if (!result.Any(x => x.Status.Tool.Id == status.Tool.Id)) result.Add(new(status, package));
        }
        foreach (var status in statusRows) Add(status, null);
        foreach (var entry in packages)
        {
            var tool = ExtensionCatalog.Tool(entry.Repository, entry.Package);
            if (tool.CustomPackage) Add(installer.Status(tool), entry);
        }
        if (!preview)
        {
            foreach (var package in NativeCommands().CachedPackages())
            {
                var module = RoBIM.Revit.Reload.NativeProtocol.Module(package.Path);
                var folder = Path.GetFileName(package.Path);
                if (result.Any(x => x.Status.Tool.FolderName.Equals(folder, StringComparison.OrdinalIgnoreCase) ||
                    x.Package is { } entry && SingleNativeButton.Registration(entry.Repository, entry.Package)?.ModuleId == module.Id)) continue;
                var spec = new ToolSpec("native-local-" + module.Id, module.Commands[0].Title, ToolKind.NativeRevit, "", "main", folder, "가져온 Revit 모듈 · " + module.Version, true);
                Add(new(spec, package.Path, package.Version, false, false, false, "기능 패키지 확인됨"), null);
            }
        }
        return result.ToArray();
    }

    private async Task ManageProduct(bool restoreBackup)
    {
        if (SelectedProduct() is not { } row) return;
        if (row.Package is not null) await ManageExtension(restoreBackup, row.Package);
        else if (restoreBackup) await RestoreSelected(row.Status.Tool.Id);
        else await RemoveSelected(row.Status.Tool.Id);
        await RefreshProductListAsync();
    }
}
