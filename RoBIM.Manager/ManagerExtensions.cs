using System.Drawing;
using System.IO;
using RoBIM.Manager.Core;

namespace RoBIM.Manager;

public sealed partial class ManagerForm
{
    private readonly ComboBox savedAccounts = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 250 };
    private readonly TextBox tokenInput = new() { Width = 320, UseSystemPasswordChar = true, PlaceholderText = "GitHub Personal Access Token" };
    private readonly TextBox gitUrl = new() { Width = 600, PlaceholderText = "https://github.com/owner/extension.git" };
    private readonly Label catalogIdentity = new() { AutoSize = true, ForeColor = Color.FromArgb(42, 96, 178), MaximumSize = new(860, 0) };
    private readonly Label catalogInfo = new() { AutoSize = true, MaximumSize = new(860, 0), ForeColor = Color.FromArgb(101, 115, 136) };
    private readonly DataGridView extensionGrid = new() { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
        AllowUserToDeleteRows = false, AllowUserToResizeRows = false, RowHeadersVisible = false, MultiSelect = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        BackgroundColor = Color.White, BorderStyle = BorderStyle.None, RowTemplate = { Height = 46 } };
    private sealed record PackageRow(ExtensionRepository Repository, ExtensionPackage Package);

    private void BuildExtensionsPage()
    {
        accountPage.AutoScroll = true;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6 };
        for (var i = 0; i < 6; i++) layout.RowStyles.Add(new(i == 2 ? SizeType.Percent : SizeType.AutoSize, i == 2 ? 100 : 0));
        var top = Row(); var refresh = MakeButton("계정 저장소 새로 고침");
        var publishers = MakeButton("제작자·조직"); publishers.Click += (_, _) => EditRepositoryPublishers();
        top.Controls.AddRange([refresh, publishers, catalogIdentity]); layout.Controls.Add(top);
        layout.Controls.Add(Note("애드인 토픽 또는 RoBIM 기본 도구만 자동 표시합니다. 다른 저장소는 Git URL로 구조를 확인하여 등록할 수 있습니다."));
        foreach (var (name, title, weight) in new[] { ("Name", "패키지", 125), ("Host", "제품", 65), ("Repo", "저장소", 180), ("Branch", "브랜치", 75), ("Status", "설치 상태", 150) })
            extensionGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = name, HeaderText = title, FillWeight = weight, SortMode = DataGridViewColumnSortMode.NotSortable });
        extensionGrid.ColumnHeadersHeight = 38; extensionGrid.EnableHeadersVisualStyles = false;
        extensionGrid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(239, 243, 249);
        extensionGrid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(80, 96, 117);
        extensionGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(230, 240, 254);
        extensionGrid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(25, 70, 130);
        extensionGrid.DefaultCellStyle.Padding = new(6, 0, 2, 0); extensionGrid.GridColor = Color.FromArgb(235, 239, 245);
        extensionGrid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal; layout.Controls.Add(extensionGrid);
        var urlRow = Row(); var register = MakeButton("URL 확인 및 등록"); urlRow.Controls.AddRange([Note("Git URL"), gitUrl, register]); layout.Controls.Add(urlRow);
        var actions = Row(); var installPackage = MakeButton("제품 화면에서 보기", true);
        var importNative = MakeButton("독립 Revit 패키지 ZIP 가져오기");
        importNative.Click += async (_, _) => await ImportNativePackage();
        actions.Controls.AddRange([installPackage, importNative]); layout.Controls.Add(actions);
        catalogInfo.Text = "지원: AutoCAD·독립 Revit .bundle. pyRevit은 검색·설치 대상에서 제외했습니다.";
        layout.Controls.Add(catalogInfo); extensionsPage.Controls.Add(layout);
        refresh.Click += async (_, _) => await Run("애드인 저장소 조회", RefreshAccountAndCatalog);
        register.Click += async (_, _) => await Run("Git URL 확인", async () =>
        {
            // Validate locally before echoing any URL into the UI or a log.
            var repository = ExtensionCatalog.Repository(gitUrl.Text);
            var entry = await ExtensionSources().Inspect("https://github.com/" + repository);
            ExtensionSources().Register(entry); gitUrl.Text = "https://github.com/" + entry.Repository + ".git";
            await RefreshExtensionCatalog();
        });
        installPackage.Click += (_, _) =>
        {
            if (SelectedPackage() is not { } row) return;
            var tool = ExtensionCatalog.Tool(row.Repository, row.Package);
            SwitchPage(tool.Kind == ToolKind.AutoCAD ? "Cad" : "Revit");
            SelectProduct(tool.Id);
        };
    }
    private void ShowCachedExtensionRows()
    {
        extensionGrid.Rows.Clear();
        foreach (var entry in availablePackages)
        {
            if (entry.Package.Host == "pyrevit") continue;
            var tool = ExtensionCatalog.Tool(entry.Repository, entry.Package);
            var status = productRows.FirstOrDefault(x => x.Status.Tool.Id == tool.Id)?.Status;
            var index = extensionGrid.Rows.Add(entry.Package.Name, tool.Kind == ToolKind.AutoCAD ? "AutoCAD" : "Revit · 독립", entry.Repository.Repository, entry.Repository.Branch, status?.Detail ?? "설치 상태 확인 전");
            extensionGrid.Rows[index].Tag = entry;
        }
    }
    private async Task RefreshAccountAndCatalog()
    {
        await RefreshAccount();
        if (!Allowed) return;
        try { await RefreshExtensionCatalog(); }
        catch (Exception ex) { catalogInfo.Text = "저장된 목록 유지 · 저장소 조회 실패: " + ex.Message; WriteLog(catalogInfo.Text); }
    }
    private async Task RefreshExtensionCatalog()
    {
        repositoryLookupCount++;
        var result = await Task.Run(() => new RepositoryDiscovery(paths).Refresh());
        availablePackages.Clear();
        foreach (var repository in result.Repositories)
            foreach (var package in repository.Packages) availablePackages.Add(new PackageRow(repository, package));
        await RefreshProductListAsync();
        ShowCachedExtensionRows();
        catalogInfo.Text = $"{result.Repositories.Count}개 애드인 저장소 · {extensionGrid.Rows.Count}개 패키지" +
            (result.Warnings.Length > 0 ? " · 확인 필요 " + result.Warnings.Length + "개 (작업 기록 참고)" : " · 일반 저장소는 표시하지 않습니다.");
        foreach (var warning in result.Warnings) WriteLog(warning);
    }
    private PackageRow? SelectedPackage() => extensionGrid.CurrentRow?.Tag as PackageRow;
    private async Task InstallExtension(PackageRow? selected = null)
    {
        var row = selected ?? SelectedPackage(); if (row is null) return;
        var tool = ExtensionCatalog.Tool(row.Repository, row.Package);
        if (tool.Kind == ToolKind.PyRevit) { MessageBox.Show(this, "pyRevit은 지원하지 않습니다. 저장소만 보관합니다.", Text); return; }
        if (tool.Kind == ToolKind.NativeRevit)
        {
            var owner = new ProductRow(installer.Status(tool), row);
            var card = ReadCommandInventory([owner]).Single();
            await ToggleButton(card, true);
            return;
        }
        if (!tool.CustomPackage)
        {
            SwitchPage(tool.Kind == ToolKind.AutoCAD ? "Cad" : "Revit");
            SelectProduct(tool.Id);
            foreach (DataGridViewRow existing in grid.Rows) if ((existing.Tag as ToolStatus)?.Tool.Id == tool.Id) grid.CurrentCell = existing.Cells[0];
            await InstallProduct(); return;
        }
        if (!Confirm(row.Package.Name + "을 설치 / 업데이트합니까?\n저장소: " + row.Repository.Repository + "\n설치 경로: " + paths.Target(tool) + "\n해당 CAD/Revit을 종료하십시오. 다운로드한 도구는 다음 제품 실행 시 실행될 수 있습니다.")) return;
        await Run("확장 패키지 설치", async () =>
        {
            EnvironmentProbe.EnsureHostClosed(tool);
            if (tool.Kind == ToolKind.PyRevit) _ = await EnvironmentProbe.RevitRoots();
            var catalog = ExtensionSources(); var package = await catalog.Download(row.Repository, row.Package);
            RequirePackageAccess(tool, package);
            await Task.Run(() => installer.Install(tool, package)); catalog.Register(row.Repository);
            await RefreshExtensionCatalog(); WriteLog("확장 설치 완료: " + row.Repository.Repository + " · " + package.Version);
        });
    }
    private async Task ManageExtension(bool restoreBackup, PackageRow? selected = null)
    {
        var row = selected ?? SelectedPackage(); if (row is null) return;
        var tool = ExtensionCatalog.Tool(row.Repository, row.Package);
        if (tool.Kind == ToolKind.PyRevit) return;
        if (tool.Kind == ToolKind.NativeRevit)
        {
            var owner = new ProductRow(installer.Status(tool), row);
            var card = ReadCommandInventory([owner]).Single();
            if (restoreBackup) { MessageBox.Show(this, "독립 버튼은 해당 카드에서 이전 버전 ZIP을 가져와 설치하십시오.", Text); return; }
            await ToggleButton(card, false);
            return;
        }
        if (!tool.CustomPackage) { MessageBox.Show(this, "RoBIM 기본 도구는 사용자 도구 화면에서 관리하십시오.", Text); return; }
        if (!Confirm(row.Package.Name + (restoreBackup ? "의 이전 버전을 복원합니까?" : "을 제거합니까?") + "\n" + paths.Target(tool))) return;
        await Run(restoreBackup ? "확장 복원" : "확장 제거", async () =>
        {
            if (restoreBackup)
            {
                var backup = installer.Backups(tool).FirstOrDefault() ?? throw new IOException("복원할 백업이 없습니다.");
                RequirePackageAccess(tool, new SourcePackage(Path.Combine(backup.Directory, "content"), backup.Record.Version));
                await Task.Run(() => installer.Restore(tool, backup));
            }
            else await Task.Run(() => installer.Remove(tool));
            await RefreshExtensionCatalog();
        });
    }
    private void ShowExtensionsPreview()
    {
        catalogIdentity.Text = "연결된 GitHub ID · 예시 사용자 / 브라우저 계정";
        extensionGrid.Rows.Add("DELHIDDEN .NET", "AutoCAD", "example/cad-tools", "main", "관리 중");
        extensionGrid.Rows.Add("Revit 도면 도구", "Revit", "example/revit-tools", "main", "설치 안 됨");
        gitUrl.Text = "https://github.com/example/revit-tools.git";
        catalogInfo.Text = "화면 확인용 예시입니다. 실제 연결 계정이나 저장소 목록을 나타내지 않습니다.";
    }
}
