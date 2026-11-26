using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using RoBIM.Manager.Core;
using WpfMessageBox = System.Windows.MessageBox;

namespace RoBIM.Manager;

public sealed partial class ManagerBackend : IDisposable
{
    private const string AppTitle = "RoBIM Add-in Manager";
    private readonly ManagerPaths paths = ManagerPaths.CurrentUser();
    private readonly InstallService installer;
    private readonly Sources sources;
    private readonly Dictionary<string, ToolStatus> statuses = [];
    private readonly Dictionary<string, string> latest = [];
    private readonly StringBuilder logBuffer = new();
    private readonly bool preview;
    private string footerText = "설치 상태를 확인하고 있습니다.";
    private string accountStatusText = "GitHub 로그인 상태를 확인하고 있습니다.";
    private string managerUpdateStatusText = "";
    private bool busy;
    private bool disposed;

    internal Window? OwnerWindow { get; set; }
    internal bool IsDisposed => disposed;

    public ManagerBackend(bool preview = false)
    {
        this.preview = preview;
        installer = new(paths, EnvironmentProbe.EnsureHostClosed);
        sources = new(paths, RequireChannelAccess);
        if (!preview) InitializeAccess();
        else ShowPreviewRows();
    }

    private async Task RefreshStatuses()
    {
        await LoadRegisteredPackages();
        var refreshed = await Task.WhenAll(Catalog.Tools.Select(tool => tool == Catalog.Revit
            ? EnvironmentProbe.RevitStatus(paths) : Task.Run(() => installer.Status(tool))));
        if (disposed) return;
        statuses.Clear();
        foreach (var status in refreshed) statuses[status.Tool.Id] = status;
        await RefreshProductListAsync();
        WriteLog("설치 상태를 확인했습니다. Revit 기존 경로는 자동 연결하며 원본을 이동하지 않습니다.");
    }

    private async Task InstallSelected(bool bundled, string? toolId = null)
    {
        var row = toolId is not null ? statuses.GetValueOrDefault(toolId) : null;
        if (row is null) return;
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
                    : await sources.DownloadCad("stable");
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

    private void ValidateCadRuntime() => EnsureHostRuntime(cad: true);

    private async Task RemoveSelected(string toolId)
    {
        if (statuses.GetValueOrDefault(toolId) is not { Managed: true } row) return;
        if (!Confirm(row.Tool.Name + "을 제거합니까?\n" + row.Path + "\n백업과 사용자 설정은 유지합니다.")) return;
        await Run("도구 제거", async () =>
        {
            await Task.Run(() => installer.Remove(row.Tool));
            WriteLog("제거했습니다. 백업에서 복원할 수 있습니다.");
            await RefreshStatuses();
        });
    }

    private async Task RestoreSelected(string toolId)
    {
        if (statuses.GetValueOrDefault(toolId) is not { } row) return;
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

    private void ShowInfo(string text, string? title = null)
    {
        if (OwnerWindow is not null && OwnerWindow.IsVisible)
            WpfMessageBox.Show(OwnerWindow, text, title ?? AppTitle, MessageBoxButton.OK, MessageBoxImage.Information);
        else
            WpfMessageBox.Show(text, title ?? AppTitle, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private bool Confirm(string text)
    {
        var result = OwnerWindow is not null && OwnerWindow.IsVisible
            ? WpfMessageBox.Show(OwnerWindow, text, "RoBIM Manager", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No)
            : WpfMessageBox.Show(text, "RoBIM Manager", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        return result == MessageBoxResult.Yes;
    }

    private async Task Run(string name, Func<Task> operation, bool requiresAccess = true)
    {
        if (busy || preview || refreshingSnapshot) return;
        if (requiresAccess && !Allowed) { ApplyAccessState(); return; }
        busy = true;
        footerText = name + " 중…";
        BackendChanged?.Invoke();
        try
        {
            await operation();
            footerText = "완료 — 제품 실행 파일과 사용자 모델은 수정하지 않습니다.";
        }
        catch (Exception ex)
        {
            footerText = "작업을 완료하지 못했습니다.";
            WriteLog("작업 중단: " + ex.Message);
            if (batchUpdating) batchErrors.Add(name + ": " + ex.Message);
            else ShowInfo(ex.Message, "RoBIM Manager");
        }
        finally
        {
            busy = false;
            ApplyAccessState();
            BackendChanged?.Invoke();
        }
    }

    private void WriteLog(string message)
    {
        if (disposed) return;
        lock (logBuffer)
        {
            logBuffer.AppendLine($"[{DateTime.Now:HH:mm:ss}] {message}");
        }
    }

    private static void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    private void OpenFolder(string directory)
    {
        if (Directory.Exists(directory))
            Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true, ArgumentList = { directory } });
        else
            ShowInfo("아직 폴더가 없습니다.", "RoBIM Manager");
    }

    private void ShowPreviewRows()
    {
        var sheetPackage = new ExtensionPackage("sheets", "시트 생성", "revit", ".", "RoBIM.Sheets.bundle", "release", "RoBIM-Sheets-Revit2026.zip", "CAD · PDF 도면목록에서 도면번호와 도면명을 읽어 Revit 시트를 생성합니다.");
        availablePackages.Add(new PackageRow(new ExtensionRepository("RoBIM-tech/RoBIM-Revit-Addin", "main", [sheetPackage], "화면 확인용 예시"), sheetPackage));
        var cad = new ToolStatus(Catalog.Cad, paths.Target(Catalog.Cad), "v1.0.0-preview.1", true, false, false, "관리 중");
        statuses[cad.Tool.Id] = cad;
        productRows.AddRange(BuildProductRows(statuses.Values.ToArray(), availablePackages.ToArray()));
        WriteLog("화면 확인용 미리보기입니다. 설치된 도구의 실제 상태를 나타내지 않습니다.");
        footerText = "RoBIM Add-in Manager · " + ManagerProductVersion;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        DisposeAccess();
        DisposeProductPolling();
        BackendClosed?.Invoke();
    }
}
