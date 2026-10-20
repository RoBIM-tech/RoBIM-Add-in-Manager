using System.Diagnostics;
using System.Text.Json;

namespace RoBIM.Manager.Core;

public sealed record ReloadPackage(string ToolId, string Source, string Version, Dictionary<string, string> Hashes,
    Dictionary<string, string> Previous, string? ExternalPath = null, string? PreviousCommit = null, string? Commit = null,
    bool ActivateSelection = false, string? SelectionSource = null, Dictionary<string, string>? SelectionSourceHashes = null);

// Preparation never changes the live extension. Only a command invoked inside the single
// matching Revit session can apply. Normal install/remove/restore still require host closure.
public sealed class RevitReload(ManagerPaths paths)
{
    private string Pending => SafeFiles.Child(paths.Data, "reload-pending.json");
    private string Profile => SafeFiles.Child(paths.Data, "reload-profile.json");
    private FileStream Lock()
    {
        SafeFiles.NoLinks(paths.Data); Directory.CreateDirectory(paths.Data);
        return new FileStream(SafeFiles.Child(paths.Data, "reload.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    private static ToolSpec Tool(string id) => id == Catalog.Revit.Id ? Catalog.Revit :
        id == Catalog.RevitDeveloper.Id ? Catalog.RevitDeveloper :
        id == Catalog.RevitSelected.Id ? Catalog.RevitSelected : throw new InvalidDataException("Revit 패키지만 Reload할 수 있습니다.");
    private static void Save<T>(string file, T value)
    {
        var temp = file + "." + Guid.NewGuid().ToString("N");
        File.WriteAllText(temp, JsonSerializer.Serialize(value)); File.Move(temp, file, true);
    }
    public void Prepare(ToolSpec tool, SourcePackage package)
    {
        using var operation = Lock(); _ = Tool(tool.Id);
        if (tool == Catalog.RevitSelected) throw new IOException("버튼 선택은 전용 준비 기능을 사용하십시오.");
        GuardSelection();
        var status = new InstallService(paths).Status(tool);
        if (status.External || status.Modified) throw new IOException("기존 설치 또는 로컬 변경 파일을 덮어쓰지 않습니다.");
        PrepareCore(tool, package, status.Managed ? SafeFiles.Hashes(status.Path) : new());
    }
    public async Task PrepareExternal(string external, SourcePackage package)
    {
        using var operation = Lock();
        GuardSelection();
        await EnvironmentProbe.ValidateExternal(external);
        if ((await ProcessRunner.GitRequired(external, "status", "--porcelain", "--untracked-files=normal")).Length != 0)
            throw new IOException("기존 Git 설치에 로컬 변경이 있습니다.");
        var previous = await ProcessRunner.GitRequired(external, "rev-parse", "HEAD");
        var branch = await ProcessRunner.GitRequired(external, "branch", "--show-current");
        var incomingBranch = await ProcessRunner.GitRequired(package.Path, "branch", "--show-current");
        if (branch != incomingBranch || branch.Length == 0) throw new IOException("현재 브랜치와 준비 브랜치가 다릅니다.");
        var commit = await ProcessRunner.GitRequired(package.Path, "rev-parse", "HEAD");
        PrepareCore(Catalog.Revit, package, SafeFiles.Hashes(external), external, previous, commit);
    }
    private void PrepareCore(ToolSpec tool, SourcePackage source, Dictionary<string, string> previous,
        string? external = null, string? previousCommit = null, string? commit = null,
        bool selection = false, string? baseline = null, Dictionary<string, string>? baselineHashes = null)
    {
        InstallService.ValidatePackage(tool, source.Path);
        var copy = SafeFiles.Child(paths.Data, "ReloadPackages/" + Guid.NewGuid().ToString("N"));
        SafeFiles.Copy(source.Path, copy, false);
        Save(Pending, new ReloadPackage(tool.Id, copy, source.Version, SafeFiles.Hashes(copy), previous, external, previousCommit, commit, selection, baseline, baselineHashes));
    }
    private void GuardSelection()
    {
        if (File.Exists(Pending) && JsonSerializer.Deserialize<ReloadPackage>(File.ReadAllText(Pending))?.ActivateSelection == true)
            throw new IOException("준비된 버튼 선택을 Revit의 적용 및 Reload에서 먼저 반영하십시오.");
    }
    public SourcePackage? PendingSelection()
    {
        using var operation = Lock();
        if (!File.Exists(Pending)) return null;
        var request = JsonSerializer.Deserialize<ReloadPackage>(File.ReadAllText(Pending));
        if (request?.ActivateSelection != true) return null;
        ValidateSelection(request);
        return new(request.Source, request.Version);
    }
    public async Task<bool> ApplyPendingSelectionWhenClosed(Func<Task> activate, Action? ensureClosed = null)
    {
        void Check() { if (ensureClosed is null) EnvironmentProbe.EnsureHostClosed(Catalog.RevitSelected); else ensureClosed(); }
        Check(); var pending = PendingSelection(); if (pending is null) return false;
        var selection = RevitButtons.ReadSelection(pending.Path)!;
        var button = selection.Buttons.FirstOrDefault() ?? throw new IOException("준비된 버튼 목록이 없습니다.");
        return await SetPendingWhenClosed(pending, button.Id, selection.Selected.Contains(button.Id), selection.Selected, activate, Check);
    }
    internal async Task<bool> SetPendingWhenClosed(SourcePackage source, string id, bool selected, IReadOnlyList<string> current, Func<Task> activate, Action ensureClosed)
    {
        using var operation = Lock(); ensureClosed();
        if (!File.Exists(Pending)) return false;
        var request = JsonSerializer.Deserialize<ReloadPackage>(File.ReadAllText(Pending)) ?? throw new IOException("준비 기록 오류");
        ValidateSelection(request);
        var selection = RevitButtons.ReadSelection(request.Source)!;
        if (!Path.GetFullPath(source.Path).Equals(Path.GetFullPath(request.Source), StringComparison.OrdinalIgnoreCase) ||
            !selection.Selected.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(current))
            throw new IOException("준비된 선택이 바뀌었습니다. 새로 고침한 뒤 다시 시도하십시오.");
        var service = new InstallService(paths);
        var status = service.Status(Catalog.RevitSelected);
        if (status.External || status.Modified || !SafeFiles.Same(request.Previous, status.Managed ? SafeFiles.Hashes(status.Path) : new()))
            throw new IOException("선택 준비 후 기존 설치가 변경되었습니다. 준비 기록과 변경 파일을 보존합니다.");
        if (request.SelectionSource is not null)
        {
            var original = service.Status(Catalog.Revit);
            var activeSource = original.Managed ? original.Path : await EnvironmentProbe.ExternalRevit(paths.Target(Catalog.Revit));
            if (activeSource is null || !Path.GetFullPath(activeSource).Equals(Path.GetFullPath(request.SelectionSource), StringComparison.OrdinalIgnoreCase) ||
                !SafeFiles.Same(request.SelectionSourceHashes!, SafeFiles.Hashes(request.SelectionSource)))
                throw new IOException("선택 준비 후 원본 설치가 변경되었습니다.");
        }
        ensureClosed();
        await new RevitButtonInstaller(paths, ensureClosed).SetCore(source, id, selected, current, activate);
        File.Delete(Pending);
        return true;
    }
    private void ValidateSelection(ReloadPackage request)
    {
        var cache = Path.GetFullPath(Path.Combine(paths.Data, "ReloadPackages")) + Path.DirectorySeparatorChar;
        if (request.ToolId != Catalog.RevitSelected.Id || (request.SelectionSource is null) != (request.SelectionSourceHashes is null) ||
            !Path.GetFullPath(request.Source).StartsWith(cache, StringComparison.OrdinalIgnoreCase) ||
            !SafeFiles.Same(request.Hashes, SafeFiles.Hashes(request.Source)) || RevitButtons.ReadSelection(request.Source) is null)
            throw new IOException("준비된 버튼 선택이 변경되었거나 경로가 올바르지 않습니다.");
    }
    public void PrepareSelection(SourcePackage source, string id, bool selected, IReadOnlyList<string> current)
    {
        using var operation = Lock();
        if (File.Exists(Profile)) throw new IOException("준비된 프로필 전환을 먼저 적용하십시오.");
        var previousRequest = File.Exists(Pending) ? JsonSerializer.Deserialize<ReloadPackage>(File.ReadAllText(Pending)) : null;
        if (previousRequest is not null && !previousRequest.ActivateSelection) throw new IOException("준비된 패키지 업데이트를 먼저 적용하십시오.");
        if (previousRequest is not null) ValidateSelection(previousRequest);
        var status = new InstallService(paths).Status(Catalog.RevitSelected);
        if (status.External || status.Modified) throw new IOException("버튼 선택 설치의 로컬 변경을 보존하기 위해 중단합니다.");
        var ids = current.ToHashSet(StringComparer.OrdinalIgnoreCase); if (selected) ids.Add(id); else ids.Remove(id);
        var output = SafeFiles.Child(paths.Data, "ButtonPackages/" + Guid.NewGuid().ToString("N"));
        var package = RevitButtons.Prepare(source, output, ids.ToArray());
        var baseline = status.Managed ? null : previousRequest?.SelectionSource ?? source.Path;
        var baselineHashes = baseline is null ? null : previousRequest?.SelectionSourceHashes ?? SafeFiles.Hashes(baseline);
        // First selection copies the existing installation to a new folder. Loaded binary bytes
        // must be identical to that installation; later changes are checked against the live selection.
        EnsureReloadable(status.Managed ? SafeFiles.Hashes(status.Path) : baselineHashes!, SafeFiles.Hashes(package.Path));
        PrepareCore(Catalog.RevitSelected, package, status.Managed ? SafeFiles.Hashes(status.Path) : new(),
            selection: true, baseline: baseline, baselineHashes: baselineHashes);
    }
    public void PrepareProfile(bool developer)
    {
        using var operation = Lock(); GuardSelection(); Save(Profile, developer ? "Developer" : "User");
    }
    public void DiscardPendingFor(string toolId)
    {
        _ = Tool(toolId); using var operation = Lock();
        if (!File.Exists(Pending)) return;
        var package = JsonSerializer.Deserialize<ReloadPackage>(File.ReadAllText(Pending)) ?? throw new IOException("준비 기록 오류");
        if (package.ToolId == toolId) File.Delete(Pending);
    }
    public static void EnsureSession(int pid, long startTicks)
    {
        var sessions = Process.GetProcessesByName("Revit");
        try
        {
            if (sessions.Length != 1 || sessions[0].Id != pid || sessions[0].StartTime.ToUniversalTime().Ticks != startTicks)
                throw new IOException("Revit을 한 개만 실행하고 해당 Revit의 RoBIM Reload 버튼에서 적용하십시오.");
        }
        finally { foreach (var process in sessions) process.Dispose(); }
    }
    public static void EnsureReloadable(Dictionary<string, string> before, Dictionary<string, string> after)
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".py", ".yaml", ".yml", ".json", ".png", ".svg", ".jpg", ".jpeg", ".bmp", ".ico", ".txt", ".md", ".csv", ".xml", ".xaml", ".html", ".css" };
        foreach (var path in before.Keys.Union(after.Keys, StringComparer.OrdinalIgnoreCase))
            if (before.GetValueOrDefault(path) != after.GetValueOrDefault(path) && !allowed.Contains(Path.GetExtension(path)))
                throw new IOException("재시작이 필요한 파일 변경: " + path + "\nRevit을 종료한 뒤 매니저에서 설치하십시오. 준비된 파일은 유지합니다.");
    }
    public async Task Apply(Action ensureSession, Func<bool, Task>? applyProfile = null,
        Func<Task>? applySelection = null, Func<string, Task>? validateSelectionSource = null)
    {
        using var operation = Lock(); ensureSession();
        if (File.Exists(Pending))
        {
            var package = JsonSerializer.Deserialize<ReloadPackage>(File.ReadAllText(Pending)) ?? throw new InvalidDataException("준비 기록 오류");
            var tool = Tool(package.ToolId);
            var cache = Path.GetFullPath(Path.Combine(paths.Data, "ReloadPackages")) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(package.Source).StartsWith(cache, StringComparison.OrdinalIgnoreCase)) throw new IOException("준비 경로 오류");
            var hashes = SafeFiles.Hashes(package.Source);
            if (!SafeFiles.Same(package.Hashes, hashes)) throw new IOException("준비된 파일이 변경되었습니다. 다시 준비하십시오.");
            if (package.ActivateSelection) ValidateSelection(package);
            else if (tool == Catalog.RevitSelected || package.SelectionSource is not null || package.SelectionSourceHashes is not null)
                throw new IOException("버튼 선택 준비 기록 오류");
            var target = paths.Target(tool);
            if (package.ActivateSelection && package.SelectionSource is not null)
            {
                if (package.Previous.Count != 0 || Directory.Exists(target) || package.SelectionSourceHashes is null)
                    throw new IOException("버튼 선택 최초 설치 상태가 변경되었습니다.");
                if (validateSelectionSource is not null) await validateSelectionSource(package.SelectionSource);
                else
                {
                    var managed = new InstallService(paths).Status(Catalog.Revit);
                    var installed = managed.Managed ? managed.Path : await EnvironmentProbe.ExternalRevit(paths.Target(Catalog.Revit));
                    if (installed is null || !Path.GetFullPath(installed).Equals(Path.GetFullPath(package.SelectionSource), StringComparison.OrdinalIgnoreCase))
                        throw new IOException("최초 버튼 선택은 현재 설치된 RoBIM 원본에서 준비하십시오. 새 바이너리는 Revit 종료 후 설치해야 합니다.");
                }
                if (!SafeFiles.Same(package.SelectionSourceHashes, SafeFiles.Hashes(package.SelectionSource)))
                    throw new IOException("버튼 선택 준비 후 원본이 변경되었습니다.");
            }
            if (package.ExternalPath is not null)
            {
                var external = await EnvironmentProbe.ExternalRevit(target);
                if (tool != Catalog.Revit || external is null || !Path.GetFullPath(external).Equals(Path.GetFullPath(package.ExternalPath), StringComparison.OrdinalIgnoreCase))
                    throw new IOException("기존 설치 연결이 변경되었습니다.");
                target = external;
            }
            void Check()
            {
                ensureSession();
                var current = Directory.Exists(target) ? SafeFiles.Hashes(target) : new();
                if (!SafeFiles.Same(package.Previous, current)) throw new IOException("준비 후 설치 파일이 변경되었습니다. 다시 준비하십시오.");
                if (package.SelectionSource is not null && !SafeFiles.Same(package.SelectionSourceHashes!, SafeFiles.Hashes(package.SelectionSource)))
                    throw new IOException("버튼 선택 준비 후 원본이 변경되었습니다.");
                EnsureReloadable(package.SelectionSourceHashes ?? current, hashes);
            }
            Check();
            if (package.ExternalPath is not null)
            {
                if (string.IsNullOrEmpty(package.PreviousCommit) || string.IsNullOrEmpty(package.Commit)) throw new IOException("Git 준비 커밋 누락");
                await EnvironmentProbe.UpdateExternal(target, paths, Check, package.PreviousCommit, package.Commit);
            }
            else
            {
                var service = new InstallService(paths, _ => Check());
                var hadPrevious = Directory.Exists(target);
                if (hadPrevious) service.InstallReload(tool, new(package.Source, package.Version));
                else service.Install(tool, new(package.Source, package.Version));
                if (package.ActivateSelection)
                {
                    try
                    {
                        ensureSession();
                        if (applySelection is null) await new RevitProfiles(paths, ensureSession).ApplySelection();
                        else await applySelection();
                    }
                    catch (Exception activation)
                    {
                        try
                        {
                            var rollback = new InstallService(paths, _ => ensureSession());
                            if (hadPrevious)
                            {
                                var backup = rollback.Backups(tool).First();
                                rollback.InstallReload(tool, new(Path.Combine(backup.Directory, "content"), backup.Record.Version));
                            }
                            else rollback.Remove(tool);
                        }
                        catch (Exception restore) { throw new IOException("버튼 선택 활성화와 복원에 실패했습니다. 백업을 확인하십시오.", new AggregateException(activation, restore)); }
                        throw;
                    }
                }
            }
            File.Delete(Pending);
        }
        if (File.Exists(Profile))
        {
            var mode = JsonSerializer.Deserialize<string>(File.ReadAllText(Profile));
            if (mode is not ("Developer" or "User")) throw new IOException("잘못된 프로필 준비 기록");
            ensureSession();
            if (mode == "Developer" && applyProfile is null)
            {
                var status = new InstallService(paths).Status(Catalog.RevitDeveloper);
                if (!status.Managed || status.Modified) throw new IOException("정상적인 관리 TEST 설치를 먼저 준비하고 적용하십시오.");
            }
            if (applyProfile is null) await new RevitProfiles(paths, ensureSession).Apply(mode == "Developer");
            else await applyProfile(mode == "Developer");
            File.Delete(Profile);
        }
    }
}
