using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text.Json;
using RoBIM.Manager.Core;

namespace RoBIM.Manager;

public sealed partial class ManagerForm
{
    private readonly Panel productsPage = new() { Dock = DockStyle.Fill };
    private readonly List<ProductRow> productRows = [];
    private string? selectedProductId;
    private readonly List<PackageRow> availablePackages = [];
    private bool authenticated;
    private string connectedLogin = "";
    private sealed record ProductRow(ToolStatus Status, PackageRow? Package);

    private void InitializeProductSelection()
    {
        var timer = new System.Windows.Forms.Timer { Interval = 1500 };
        timer.Tick += async (_, _) => { if (currentPage == "Locations") UpdateLocationActions(); else if (IsProductPage || currentPage == "Ribbon") await PollLocalChanges(); };
        if (!preview) timer.Start();
        FormClosed += (_, _) => timer.Dispose();
    }
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
                var spec = new ToolSpec("native-local-" + module.Id, module.Commands[0].Title, ToolKind.NativeRevit, "", "main", folder, "가져온 독립 Revit 모듈 · " + module.Version, true);
                Add(new(spec, package.Path, package.Version, false, false, false, "기능 패키지 확인됨"), null);
            }
        }
        return result.ToArray();
    }
    private void RefreshProductList()
    {
        // Fixture rendering only. Real I/O is awaited on a worker in RefreshProductListAsync.
        if (preview) { productRows.Clear(); productRows.AddRange(BuildProductRows(statuses.Values.ToArray(), availablePackages.ToArray())); }
        RenderProductCards();
    }    private void RefreshProductAction() => RefreshCardActions();
    private void SelectLegacy(string id)
    {
        foreach (DataGridViewRow row in grid.Rows) if ((row.Tag as ToolStatus)?.Tool.Id == id) { grid.CurrentCell = row.Cells[0]; return; }
        throw new IOException("설치 상태를 새로 고친 뒤 다시 시도하십시오.");
    }
    private async Task InstallProduct()
    {
        if (SelectedProduct() is not { } row) return;
        if (CadConfigurable(row)) { await ConfigureCadButtons(false); return; }
        if (CadNeedsConnection(row)) { SelectLegacy(Catalog.Cad.Id); await InstallSelected(true); return; }
        var step = InstallFlow.Decide(row.Status, HostRunning(row.Status.Tool.Kind), authenticated, PendingFor(row.Status.Tool.Id));
        switch (step.Action)
        {
            case InstallAction.Connect: SwitchPage("Account"); return;
            case InstallAction.WaitForHost: case InstallAction.Blocked: return;
            case InstallAction.ApplyInRevit: ShowReloadReady(); return;
        }
        if (row.Package is not null) await InstallExtension(row.Package);
        else
        {
            SelectLegacy(row.Status.Tool.Id);
            if (step.Action == InstallAction.PrepareRevit) await PrepareUserReload();
            else await InstallSelected(step.Action == InstallAction.BundledCad);
        }
        await RefreshProductListAsync();
    }
    private async Task ManageProduct(bool restoreBackup)
    {
        if (SelectedProduct() is not { } row) return;
        if (row.Package is not null) await ManageExtension(restoreBackup, row.Package);
        else { SelectLegacy(row.Status.Tool.Id); if (restoreBackup) await RestoreSelected(); else await RemoveSelected(); }
        await RefreshProductListAsync();
    }
}
