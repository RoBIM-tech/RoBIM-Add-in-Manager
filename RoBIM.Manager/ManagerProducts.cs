using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text.Json;
using RoBIM.Manager.Core;

namespace RoBIM.Manager;

public sealed partial class ManagerForm
{
    private readonly Panel productsPage = new() { Dock = DockStyle.Fill };
    private readonly DataGridView productList = new() { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
        AllowUserToDeleteRows = false, RowHeadersVisible = false, MultiSelect = false, AllowUserToResizeRows = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        BackgroundColor = Color.White, BorderStyle = BorderStyle.None, RowTemplate = { Height = 64 } };
    private readonly Label nextStep = new() { Dock = DockStyle.Fill, Font = new Font("맑은 고딕", 12), ForeColor = Color.FromArgb(35, 74, 123), Padding = new(12), AutoSize = true, MaximumSize = new(900, 0) };
    private readonly Label selectedInfo = new() { AutoSize = true, ForeColor = Color.FromArgb(90, 106, 126), MaximumSize = new(890, 0), Padding = new(0, 8, 0, 8) };
    private readonly Button productInstall = MakeButton("설치하기", true);
    private readonly List<PackageRow> availablePackages = [];
    private bool authenticated;
    private string connectedLogin = "";
    private sealed record ProductRow(ToolStatus Status, PackageRow? Package);

    private void BuildProductsPage()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6 };
        layout.RowStyles.Add(new(SizeType.AutoSize)); layout.RowStyles.Add(new(SizeType.AutoSize));
        layout.RowStyles.Add(new(SizeType.Percent, 100));
        for (var i = 3; i < 6; i++) layout.RowStyles.Add(new(SizeType.AutoSize));
        layout.Controls.Add(Note("1  도구 선택       →       2  설치 버튼       →       3  제품에서 실행"));
        var refreshRow = Row(); var refresh = MakeButton("새로 고침"); var add = MakeButton("도구 추가");
        refreshRow.Controls.AddRange([refresh, add]); layout.Controls.Add(refreshRow);
        foreach (var (name, title, weight) in new[] { ("Tool", "도구", 180), ("Status", "현재 상태", 170) })
            productList.Columns.Add(new DataGridViewTextBoxColumn { Name = name, HeaderText = title, FillWeight = weight, SortMode = DataGridViewColumnSortMode.NotSortable });
        productList.ColumnHeadersHeight = 40; productList.EnableHeadersVisualStyles = false;
        productList.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(239, 243, 249);
        productList.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(239, 243, 249);
        productList.DefaultCellStyle.Padding = new(14, 0, 8, 0);
        productList.DefaultCellStyle.SelectionBackColor = Color.FromArgb(230, 240, 254);
        productList.DefaultCellStyle.SelectionForeColor = Color.FromArgb(25, 70, 130);
        productList.GridColor = Color.FromArgb(235, 239, 245); productList.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        layout.Controls.Add(productList); layout.Controls.Add(selectedInfo);
        var stepCard = new Panel { Dock = DockStyle.Fill, AutoSize = true, BackColor = Color.FromArgb(232, 241, 253), Padding = new(4) };
        stepCard.Controls.Add(nextStep); layout.Controls.Add(stepCard);
        var buttons = Row(); productInstall.MinimumSize = new(220, 48); var more = MakeButton("추가 작업 ▾");
        buttons.Padding = new(0, 18, 0, 0); buttons.Controls.AddRange([productInstall, more]); layout.Controls.Add(buttons);
        productsPage.Controls.Add(layout);
        var menu = new ContextMenuStrip();
        menu.Items.Add("설치 폴더 열기", null, (_, _) => { if (SelectedProduct() is { } row) OpenFolder(row.Status.Path); });
        menu.Items.Add("이전 버전 복원", null, async (_, _) => await ManageProduct(true));
        menu.Items.Add("도구 제거", null, async (_, _) => await ManageProduct(false));
        menu.Items.Add("Revit 일반 도구로 돌아가기", null, async (_, _) => await ApplyProfile(false));
        menu.Items.Add("Git URL로 도구 추가", null, (_, _) => SwitchPage("Extensions"));
        menu.Items.Add("GitHub 계정 / 토큰", null, (_, _) => SwitchPage("Account"));
        menu.Items.Add("CAD 동봉 버전 설치", null, async (_, _) => { if (SelectedProduct()?.Status.Tool.Id == Catalog.Cad.Id) { SelectLegacy(Catalog.Cad.Id); await InstallSelected(true); } });
        menu.Opening += (_, _) =>
        {
            var status = SelectedProduct()?.Status;
            menu.Items[1].Enabled = status is { External: false, Modified: false };
            menu.Items[2].Enabled = status is { Managed: true, Modified: false };
            menu.Items[3].Visible = currentPage == "Revit";
            menu.Items[6].Visible = status?.Tool.Id == Catalog.Cad.Id;
        };
        more.Click += (_, _) => menu.Show(more, new Point(0, more.Height));
        productList.SelectionChanged += (_, _) => RefreshProductAction();
        productInstall.Click += async (_, _) => await InstallProduct();
        add.Click += (_, _) => SwitchPage("Extensions");
        refresh.Click += async (_, _) => await Run("설치 상태 확인", async () => { await RefreshStatuses(); await RefreshAccountAndCatalog(); });
        var timer = new System.Windows.Forms.Timer { Interval = 1500 };
        timer.Tick += (_, _) => { if (!busy && currentPage is "Revit" or "Cad") RefreshProductAction(); };
        if (!preview) timer.Start(); FormClosed += (_, _) => timer.Dispose();
    }
    private ProductRow? SelectedProduct() => productList.CurrentRow?.Tag as ProductRow;
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
    private void RefreshProductList()
    {
        if (currentPage is not ("Revit" or "Cad")) return;
        var kind = currentPage == "Cad" ? ToolKind.AutoCAD : ToolKind.PyRevit;
        var selected = SelectedProduct()?.Status.Tool.Id;
        productList.Rows.Clear();
        foreach (var status in statuses.Values.Where(x => x.Tool.Kind == kind || kind == ToolKind.PyRevit && x.Tool.Kind == ToolKind.NativeRevit)) Add(status, null);
        foreach (var entry in availablePackages)
        {
            var tool = ExtensionCatalog.Tool(entry.Repository, entry.Package);
            if (!tool.CustomPackage || (tool.Kind != kind && !(kind == ToolKind.PyRevit && tool.Kind == ToolKind.NativeRevit))) continue;
            Add(installer.Status(tool), entry);
        }
        void Add(ToolStatus status, PackageRow? package)
        {
            var text = status.Modified ? "확인 필요" : status.Managed ? "설치됨" : status.External ? "기존 설치 사용 중" : "설치 안 됨";
            var index = productList.Rows.Add(status.Tool.Name, text);
            productList.Rows[index].Tag = new ProductRow(status, package);
            if (status.Tool.Id == selected) productList.CurrentCell = productList.Rows[index].Cells[0];
        }
        RefreshProductAction();
    }
    private void RefreshProductAction()
    {
        if (SelectedProduct() is not { } row) { nextStep.Text = "설치 상태를 확인하고 있습니다."; productInstall.Enabled = false; return; }
        var running = !preview && HostRunning(row.Status.Tool.Kind);
        var step = InstallFlow.Decide(row.Status, running, preview || authenticated, !preview && PendingFor(row.Status.Tool.Id));
        selectedInfo.Text = row.Status.Tool.Name + (row.Status.Managed || row.Status.External ? "  ·  사용 준비됨" : "  ·  설치할 도구") +
            Environment.NewLine + (connectedLogin.Length > 0 ? "GitHub 연결: " + connectedLogin : "GitHub 계정은 설정에서 연결할 수 있습니다.");
        nextStep.Text = step.Instruction; productInstall.Text = step.Button; productInstall.Enabled = !busy && step.Enabled;
    }
    private void SelectLegacy(string id)
    {
        foreach (DataGridViewRow row in grid.Rows) if ((row.Tag as ToolStatus)?.Tool.Id == id) { grid.CurrentCell = row.Cells[0]; return; }
        throw new IOException("설치 상태를 새로 고친 뒤 다시 시도하십시오.");
    }
    private async Task InstallProduct()
    {
        if (SelectedProduct() is not { } row) return;
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
        RefreshProductList();
    }
    private async Task ManageProduct(bool restoreBackup)
    {
        if (SelectedProduct() is not { } row) return;
        if (row.Package is not null) await ManageExtension(restoreBackup, row.Package);
        else { SelectLegacy(row.Status.Tool.Id); if (restoreBackup) await RestoreSelected(); else await RemoveSelected(); }
        RefreshProductList();
    }
}
