using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text.Json;
using RoBIM.Manager.Core;

namespace RoBIM.Manager;

public sealed partial class ManagerForm : Form
{
    private readonly ManagerPaths paths = ManagerPaths.CurrentUser();
    private readonly InstallService installer;
    private readonly Sources sources;
    private readonly Dictionary<string, ToolStatus> statuses = [];
    private readonly Dictionary<string, string> latest = [];
    private readonly DataGridView grid = new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false, RowHeadersVisible = false, MultiSelect = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BackgroundColor = Color.White,
        BorderStyle = BorderStyle.None, RowTemplate = { Height = 44 },
        AutoGenerateColumns = false, EnableHeadersVisualStyles = false
    };
    private readonly TextBox details = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
        BorderStyle = BorderStyle.None, BackColor = Color.FromArgb(244, 247, 251), ScrollBars = ScrollBars.Vertical };
    private readonly TextBox log = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
        BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle, ScrollBars = ScrollBars.Vertical };
    private readonly Label footer = new() { Dock = DockStyle.Fill, AutoSize = true, Text = "설치 상태를 확인하고 있습니다.", Padding = new(0, 8, 0, 0) };
    private readonly FlowLayoutPanel actions = new() { Dock = DockStyle.Fill, AutoSize = true, Padding = new(0, 8, 0, 8) };
    private readonly FlowLayoutPanel toolbar = new() { Dock = DockStyle.Fill, AutoSize = true, Padding = new(0, 0, 0, 12) };
    private readonly Button install = MakeButton("GitHub 최신 설치 / 업데이트", true);
    private readonly Button offline = MakeButton("동봉 CAD 설치");
    private readonly Button restore = MakeButton("이전 버전 복원");
    private readonly Button remove = MakeButton("제거");
    private bool busy;
    private readonly bool preview;

    public ManagerForm(bool preview = false, bool inspect = false)
    {
        this.preview = preview;
        installer = new(paths, EnvironmentProbe.EnsureHostClosed);
        sources = new(paths, RequireChannelAccess);
        Text = "RoBIM Add-in Manager";
        using (var stream = typeof(ManagerForm).Assembly.GetManifestResourceStream("RoBIM.Manager.AppIcon.ico")!)
        using (var icon = new Icon(stream)) Icon = (Icon)icon.Clone();
        Font = new Font("맑은 고딕", 10F);
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        Size = new Size(1320, 850);
        MinimumSize = new Size(1180, 740);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.White;
        BuildView();
        BuildLoginPage();
        if (!preview && !inspect) InitializeAccess();
        FormClosing += (_, e) => { if (busy) { e.Cancel = true; MessageBox.Show(this, "작업이 끝난 뒤 닫아 주십시오.", Text); } };
        if (preview) { ShowPreviewRows(); _ = RefreshProfileLabel(); }
        else if (!inspect) Shown += async (_, _) => { await StartLocalDisplay(); await RestoreAccountAccess(); };
    }
    private static Button MakeButton(string text, bool primary = false) => new()
    {
        Text = text, AutoSize = true, MinimumSize = new(90, 36), Margin = new(0, 0, 8, 0),
        FlatStyle = FlatStyle.Flat, BackColor = primary ? Color.FromArgb(30, 91, 166) : Color.White,
        ForeColor = primary ? Color.White : Color.FromArgb(24, 46, 78), Padding = new(5)
    };

    private void AddColumn(string name, string title, int weight) => grid.Columns.Add(
        new DataGridViewTextBoxColumn { Name = name, HeaderText = title, FillWeight = weight, SortMode = DataGridViewColumnSortMode.NotSortable });

    private ToolStatus? SelectedStatus() => grid.CurrentRow?.Tag as ToolStatus;
    private void ShowSelected()
    {
        if (SelectedStatus() is not { } row) return;
        details.Text = row.Tool.Description + Environment.NewLine + "설치 경로: " + row.Path + Environment.NewLine +
            (row.External
                ? "기존 설치 연결: 현재 브랜치와 로컬 변경을 보존합니다. 업데이트는 깨끗한 Git 작업 사본에서만 수행합니다. 기존 설치의 제거·자동 복원은 지원하지 않습니다."
                : "관리 설치: 업데이트 전 백업 · 파일 변경 시 교체 차단 · 제거 후에도 백업 유지. 적용하려면 해당 CAD/Revit을 종료해야 합니다.");
        install.Enabled = !busy && !row.Modified && (row.Tool.Kind != ToolKind.AutoCAD || !row.External) &&
            !(row.External && row.Tool.Kind == ToolKind.PyRevit && !row.Version.StartsWith("main@", StringComparison.Ordinal));
        offline.Enabled = !busy && row.Tool.Kind == ToolKind.AutoCAD && !row.External && !row.Modified;
        remove.Enabled = !busy && row.Managed && !row.Modified;
        restore.Enabled = !busy && !row.External && !row.Modified;
    }

    private async Task RefreshStatuses()
    {
        await LoadRegisteredPackages();
        var selectedId = SelectedStatus()?.Tool.Id;
        var refreshed = await Task.WhenAll(Catalog.Tools.Select(tool => tool == Catalog.Revit
            ? EnvironmentProbe.RevitStatus(paths) : Task.Run(() => installer.Status(tool))));
        if (IsDisposed) return;
        statuses.Clear();
        foreach (var status in refreshed) statuses[status.Tool.Id] = status;
        grid.Rows.Clear();
        foreach (var row in statuses.Values)
        {
            var index = grid.Rows.Add(row.Tool.Kind == ToolKind.AutoCAD ? "AutoCAD" : "Revit", row.Tool.Name,
                row.Version, latest.GetValueOrDefault(row.Tool.Id, "확인 전"), row.Detail);
            grid.Rows[index].Tag = row;
            if (row.Tool.Id == selectedId) grid.CurrentCell = grid.Rows[index].Cells[0];
        }
        if (!preview && cadButtonPackage is null && File.Exists(Path.Combine(AppContext.BaseDirectory, "DelHiddenNet-AutoCAD2026.zip")))
            cadButtonPackage = await Task.Run(() => sources.OfflineCad(Path.Combine(AppContext.BaseDirectory, "DelHiddenNet-AutoCAD2026.zip")));
        ShowSelected();
        await RefreshProductListAsync();
        WriteLog("설치 상태를 확인했습니다. Revit 기존 경로는 자동 연결하며 원본을 이동하지 않습니다.");
    }

    private async Task CheckLatest()
    {
        if (SelectedStatus() is not { } row) return;
        if (row.Tool.Kind == ToolKind.AutoCAD)
        {
            // Reuse release selection, validation, and checksum verification for a useful cached download.
            var package = await sources.DownloadCad(CanUseTest ? null : "stable");
            latest[row.Tool.Id] = package.Version;
        }
        else
        {
            var branch = row.External ? await ProcessRunner.GitRequired(row.Path, "branch", "--show-current") : row.Tool.Branch;
            if (branch.Length == 0) throw new IOException("분리된 HEAD 상태입니다. 기존 브랜치를 먼저 확인하십시오.");
            var result = await ProcessRunner.Run("git", ["ls-remote", "https://github.com/" + row.Tool.Repository + ".git", "refs/heads/" + branch]);
            var sha = result.Output.Split(['\t', ' ', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (result.ExitCode != 0 || sha is null || sha.Length != 40) throw new IOException("Revit 저장소의 최신 커밋을 확인할 수 없습니다.");
            latest[row.Tool.Id] = branch + "@" + sha[..12];
        }
        await RefreshStatuses();
        WriteLog("원격 최신: " + latest[row.Tool.Id] + " — 미리보기 릴리스도 포함합니다.");
    }

    private async Task InstallSelected(bool bundled)
    {
        if (SelectedStatus() is not { } row) return;
        if (!Confirm((row.External ? "기존 Revit 설치를 현재 추적 브랜치로 업데이트합니다." : "선택한 도구를 설치 / 업데이트합니다.") +
            "\n\n" + row.Tool.Name + "\n" + row.Path + "\n\n해당 제품을 종료해야 적용할 수 있습니다. 기존 파일은 교체 전에 백업합니다.")) return;
        await Run("설치 / 업데이트", async () =>
        {
            EnvironmentProbe.EnsureHostClosed(row.Tool);
            if (row.External)
            {
                if (row.Tool.Kind != ToolKind.PyRevit) throw new IOException("기존 CAD 설치를 덮어쓰지 않습니다.");
                WriteLog(await EnvironmentProbe.UpdateExternal(row.Path, paths));
            }
            else
            {
                if (row.Tool.Kind == ToolKind.AutoCAD) ValidateCadRuntime();
                else
                {
                    _ = await EnvironmentProbe.RevitRoots();
                    if (await EnvironmentProbe.ExternalRevit(paths.Target(row.Tool)) is not null)
                        throw new IOException("기존 Revit 설치가 있어 중복 설치하지 않습니다. 상태를 새로 고치십시오.");
                }
                var package = row.Tool.Kind == ToolKind.PyRevit ? await sources.DownloadRevit()
                    : bundled ? await sources.OfflineCad(Path.Combine(AppContext.BaseDirectory, "DelHiddenNet-AutoCAD2026.zip"))
                    : await sources.DownloadCad(CanUseTest ? null : "stable");
                if (row.Tool.Kind == ToolKind.PyRevit)
                    package = await Task.Run(() => RevitPackages.Prepare(package, SafeFiles.Child(paths.Data, "Prepared/" + Guid.NewGuid().ToString("N")), false));
                RequirePackageAccess(row.Tool, package);
                await Task.Run(() => installer.Install(row.Tool, package));
                WriteLog("설치 완료: " + package.Version + ". 제품을 다시 시작하면 적용됩니다.");
            }
            if (row.Tool.Kind == ToolKind.PyRevit) new RevitReload(paths).DiscardPendingFor(row.Tool.Id);
            await RefreshStatuses();
        });
    }

    private void ValidateCadRuntime()
    {
        EnsureHostRuntime(cad: true);
    }

    private async Task RemoveSelected()
    {
        if (SelectedStatus() is not { Managed: true } row || !Confirm(row.Tool.Name + "을 제거합니까?\n" + row.Path + "\n백업과 사용자 설정은 유지합니다.")) return;
        await Run("도구 제거", async () =>
        { await Task.Run(() => installer.Remove(row.Tool)); WriteLog("제거했습니다. 백업에서 복원할 수 있습니다."); await RefreshStatuses(); });
    }

    private async Task RestoreSelected()
    {
        if (SelectedStatus() is not { } row) return;
        await Run("이전 버전 복원", async () =>
        {
            var backup = installer.Backups(row.Tool).FirstOrDefault() ?? throw new IOException("복원할 백업이 없습니다.");
            if (!Confirm(row.Tool.Name + "을 " + backup.Record.Version + " 버전으로 복원합니까?\n현재 버전도 먼저 백업합니다.")) return;
            RequirePackageAccess(row.Tool, new SourcePackage(Path.Combine(backup.Directory, "content"), backup.Record.Version));
            await Task.Run(() => installer.Restore(row.Tool, backup));
            WriteLog("이전 버전을 복원했습니다. 제품을 다시 시작하십시오.");
            await RefreshStatuses();
        });
    }

    private bool Confirm(string text) => MessageBox.Show(this, text, "RoBIM Manager", MessageBoxButtons.YesNo,
        MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;

    private async Task Run(string name, Func<Task> operation, bool requiresAccess = true)
    {
        if (busy || preview || refreshingSnapshot) return;
        if (requiresAccess && !Allowed) { SwitchPage("Account"); ApplyAccessState(); return; }
        busy = true;
        UpdateLoginPage();
        toolbar.Enabled = actions.Enabled = grid.Enabled = false;
        userPage.Enabled = developerPage.Enabled = accountPage.Enabled = extensionsPage.Enabled = productsPage.Enabled = locationsPage.Enabled = ribbonPage.Enabled = false;
        foreach (var control in navigation) control.Enabled = false;
        footer.Text = name + " 중…";
        try { await operation(); footer.Text = "완료 — 제품 실행 파일과 사용자 모델은 수정하지 않습니다."; }
        catch (Exception ex)
        {
            footer.Text = "작업을 완료하지 못했습니다.";
            WriteLog("작업 중단: " + ex.Message);
            MessageBox.Show(this, ex.Message, "RoBIM Manager", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        finally
        {
            busy = false; toolbar.Enabled = actions.Enabled = grid.Enabled = true;
            userPage.Enabled = developerPage.Enabled = accountPage.Enabled = extensionsPage.Enabled = productsPage.Enabled = locationsPage.Enabled = ribbonPage.Enabled = true;
            foreach (var control in navigation) control.Enabled = true;
            ShowSelected();
            RefreshProductAction();
            if (currentPage == "Locations") UpdateLocationActions();
            ApplyAccessState();
        }
    }

    private void WriteLog(string message)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { try { BeginInvoke(() => WriteLog(message)); } catch (InvalidOperationException) { } return; }
        log.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    }
    private static void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    private static void OpenFolder(string directory)
    {
        if (Directory.Exists(directory)) Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true, ArgumentList = { directory } });
        else MessageBox.Show("아직 폴더가 없습니다.", "RoBIM Manager");
    }

    private void ShowPreviewRows()
    {
        var sheetPackage = new ExtensionPackage("sheets", "RoBIM 시트 생성", "revit", ".", "RoBIM.Sheets.bundle", "release", "RoBIM-Sheets-Revit2026.zip", "CAD · PDF 도면목록에서 도면번호와 도면명을 읽어 Revit 시트를 생성합니다.");
        availablePackages.Add(new PackageRow(new ExtensionRepository("RoBIM-tech/RoBIM-Revit-Addin", "main", [sheetPackage], "화면 확인용 예시"), sheetPackage));
        var cad = new ToolStatus(Catalog.Cad, paths.Target(Catalog.Cad), "v1.0.0-preview.1", true, false, false, "관리 중");
        var revit = new ToolStatus(Catalog.Revit, @"C:\RoBIM\RoBIM.extension", "main@82b51a7e0d42", false, true, false, "기존 설치 연결 · 브랜치 유지");
        foreach (var row in new[] { cad, revit })
        {
            statuses[row.Tool.Id] = row;
            var index = grid.Rows.Add(row.Tool.Kind == ToolKind.AutoCAD ? "AutoCAD" : "Revit", row.Tool.Name, row.Version, "확인 전", row.Detail);
            grid.Rows[index].Tag = row;
        }
        ShowSelected();
        RefreshProductList();
        WriteLog("화면 확인용 미리보기입니다. 설치된 도구의 실제 상태를 나타내지 않습니다.");
        footer.Text = "RoBIM Add-in Manager · " + Application.ProductVersion.Split('+')[0];
    }

    public void RenderPreview(string destination)
    {
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Location = new Point(-20000, -20000);
        Show();
        Application.DoEvents();
        PerformLayout();
        using var bitmap = new Bitmap(Width, Height);
        DrawToBitmap(bitmap, new Rectangle(Point.Empty, Size));
        bitmap.Save(destination, System.Drawing.Imaging.ImageFormat.Png);
        Close();
    }
}
