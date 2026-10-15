using System.Diagnostics;
using System.Text.Json;

namespace RoBIM.Manager.Core;

public sealed record ReloadPackage(string ToolId, string Source, string Version, Dictionary<string, string> Hashes,
    Dictionary<string, string> Previous, string? ExternalPath = null, string? PreviousCommit = null, string? Commit = null);

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
        id == Catalog.RevitDeveloper.Id ? Catalog.RevitDeveloper : throw new InvalidDataException("Revit 패키지만 Reload할 수 있습니다.");
    private static void Save<T>(string file, T value)
    {
        var temp = file + "." + Guid.NewGuid().ToString("N");
        File.WriteAllText(temp, JsonSerializer.Serialize(value)); File.Move(temp, file, true);
    }
    public void Prepare(ToolSpec tool, SourcePackage package)
    {
        using var operation = Lock(); _ = Tool(tool.Id);
        var status = new InstallService(paths).Status(tool);
        if (status.External || status.Modified) throw new IOException("기존 설치 또는 로컬 변경 파일을 덮어쓰지 않습니다.");
        PrepareCore(tool, package, status.Managed ? SafeFiles.Hashes(status.Path) : new());
    }
    public async Task PrepareExternal(string external, SourcePackage package)
    {
        using var operation = Lock();
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
        string? external = null, string? previousCommit = null, string? commit = null)
    {
        InstallService.ValidatePackage(tool, source.Path);
        var copy = SafeFiles.Child(paths.Data, "ReloadPackages/" + Guid.NewGuid().ToString("N"));
        SafeFiles.Copy(source.Path, copy, false);
        Save(Pending, new ReloadPackage(tool.Id, copy, source.Version, SafeFiles.Hashes(copy), previous, external, previousCommit, commit));
    }
    public void PrepareProfile(bool developer)
    {
        using var operation = Lock(); Save(Profile, developer ? "Developer" : "User");
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
    public async Task Apply(Action ensureSession, Func<bool, Task>? applyProfile = null)
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
            var target = paths.Target(tool);
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
                EnsureReloadable(current, hashes);
            }
            Check();
            if (package.ExternalPath is not null)
            {
                if (string.IsNullOrEmpty(package.PreviousCommit) || string.IsNullOrEmpty(package.Commit)) throw new IOException("Git 준비 커밋 누락");
                await EnvironmentProbe.UpdateExternal(target, paths, Check, package.PreviousCommit, package.Commit);
            }
            else new InstallService(paths, _ => Check()).Install(tool, new(package.Source, package.Version));
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
