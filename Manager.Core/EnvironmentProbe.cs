using System.Diagnostics;

namespace RoBIM.Manager.Core;

public static class EnvironmentProbe
{
    public static void EnsureHostClosed(ToolSpec tool)
    {
        var name = tool.Kind == ToolKind.AutoCAD ? "acad" : "Revit";
        var processes = Process.GetProcessesByName(name);
        try
        {
            if (processes.Length > 0)
                throw new InvalidOperationException($"{(tool.Kind == ToolKind.AutoCAD ? "AutoCAD" : "Revit")}가 실행 중입니다. 저장하고 종료한 뒤 다시 시도하십시오. 실행 중인 프로그램을 강제로 닫지 않습니다.");
        }
        finally { foreach (var process in processes) process.Dispose(); }
        if (tool.Kind == ToolKind.AutoCAD)
        {
            var core = Process.GetProcessesByName("accoreconsole");
            try { if (core.Length > 0) throw new InvalidOperationException("AutoCAD Core Console을 종료한 뒤 다시 시도하십시오."); }
            finally { foreach (var process in core) process.Dispose(); }
        }
    }

    public static async Task<IReadOnlyList<string>> RevitRoots()
    {
        var roots = new List<string> { ManagerPaths.CurrentUser().RevitExtensions };
        var result = await ProcessRunner.Run("pyrevit", ["extensions", "paths"]);
        if (result.ExitCode != 0) throw new IOException("pyRevit CLI에 접근할 수 없습니다. pyRevit 설치와 Revit 연결을 확인하십시오.");
        foreach (var line in result.Output.Split('\n').Select(x => x.Trim()))
            if (Path.IsPathFullyQualified(line) && Directory.Exists(line)) roots.Add(line);
        return roots.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static async Task<string?> ExternalRevit(string managedPath)
    {
        foreach (var root in await RevitRoots())
        {
            var path = Path.Combine(root, Catalog.Revit.FolderName);
            if (Directory.Exists(path) && !Path.GetFullPath(path).Equals(Path.GetFullPath(managedPath), StringComparison.OrdinalIgnoreCase))
            {
                SafeFiles.NoLinks(path);
                return path;
            }
        }
        return null;
    }

    public static async Task<string> ExternalVersion(string path)
    {
        await ValidateExternal(path);
        var branch = await ProcessRunner.GitRequired(path, "branch", "--show-current");
        var sha = await ProcessRunner.GitRequired(path, "rev-parse", "HEAD");
        return branch + "@" + sha[..12];
    }

    public static async Task ValidateExternal(string path)
    {
        SafeFiles.NoLinks(path);
        if (!Directory.Exists(Path.Combine(path, ".git"))) throw new IOException("기존 설치가 Git 작업 사본이 아닙니다.");
        var top = await ProcessRunner.GitRequired(path, "rev-parse", "--show-toplevel");
        if (!Path.GetFullPath(top).Equals(Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
            throw new IOException("기존 설치의 Git 루트가 일치하지 않습니다.");
        var remote = await ProcessRunner.GitRequired(path, "remote", "get-url", "origin");
        if (!remote.Equals("https://github.com/gagaggan/RoBIM-pyrevit.git", StringComparison.OrdinalIgnoreCase))
            throw new IOException("기존 설치의 저장소 주소가 RoBIM 저장소와 다릅니다.");
    }

    public static async Task<string> UpdateExternal(string path, ManagerPaths paths, Action? ensureHostClosed = null,
        string? expectedLocal = null, string? expectedRemote = null)
    {
        void CheckHost() { if (ensureHostClosed is null) EnsureHostClosed(Catalog.Revit); else ensureHostClosed(); }
        CheckHost();
        await ValidateExternal(path);
        var branch = await ProcessRunner.GitRequired(path, "branch", "--show-current");
        if (branch.Length == 0) throw new IOException("분리된 HEAD 상태에서는 기존 설치를 업데이트하지 않습니다.");
        var status = await ProcessRunner.GitRequired(path, "status", "--porcelain", "--untracked-files=normal");
        if (status.Length != 0) throw new IOException("기존 설치에 로컬 변경이 있어 업데이트하지 않습니다. 작업 파일을 먼저 보관하십시오.");
        var upstream = await ProcessRunner.GitRequired(path, "rev-parse", "--abbrev-ref", "@{upstream}");
        if (upstream != "origin/" + branch) throw new IOException("현재 브랜치의 origin 추적 설정을 먼저 확인하십시오.");
        var previous = await ProcessRunner.GitRequired(path, "rev-parse", "HEAD");
        if (expectedLocal is not null && previous != expectedLocal) throw new IOException("준비 후 기존 커밋이 변경되었습니다. 업데이트를 다시 준비하십시오.");
        await ProcessRunner.GitRequired(path, "fetch", "origin", branch);
        // Never switch branches, reset, stash, force-pull, or remove untracked files.
        var fetch = await ProcessRunner.GitRequired(path, "rev-parse", "FETCH_HEAD");
        if (expectedRemote is not null && fetch != expectedRemote) throw new IOException("준비 후 원격 커밋이 변경되었습니다. 업데이트를 다시 준비하십시오.");
        if (fetch == previous) return "이미 최신 상태입니다.";
        var isAncestor = await ProcessRunner.Git(path, "merge-base", "--is-ancestor", previous, fetch);
        if (isAncestor.ExitCode != 0) throw new IOException("원격과 로컬 이력이 갈라져 자동 업데이트하지 않습니다.");
        CheckHost();
        if ((await ProcessRunner.GitRequired(path, "status", "--porcelain", "--untracked-files=normal")).Length != 0)
            throw new IOException("다운로드 중 로컬 변경이 생겨 업데이트하지 않습니다.");
        var backup = SafeFiles.Child(paths.Data, "ExternalBackups/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
        SafeFiles.Copy(path, Path.Combine(backup, "content"));
        File.WriteAllText(Path.Combine(backup, "previous-commit.txt"), previous);
        await ProcessRunner.GitRequired(path, "merge", "--ff-only", "--no-edit", fetch);
        return "기존 브랜치를 유지하며 업데이트했습니다. 이전 사본: " + backup;
    }
}
