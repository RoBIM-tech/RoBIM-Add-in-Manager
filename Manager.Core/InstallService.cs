using System.Text.Json;

namespace RoBIM.Manager.Core;

public sealed class InstallService(ManagerPaths paths, Action<ToolSpec>? ensureHostClosed = null)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public ManagerPaths Paths => paths;
    private string StatePath => Path.Combine(paths.Data, "installed.json");

    private FileStream Lock()
    {
        SafeFiles.NoLinks(paths.Data);
        Directory.CreateDirectory(paths.Data);
        return new FileStream(Path.Combine(paths.Data, "operation.lock"), FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
    }

    public Dictionary<string, InstallRecord> ReadState()
    {
        SafeFiles.NoLinks(StatePath);
        return File.Exists(StatePath)
            ? JsonSerializer.Deserialize<Dictionary<string, InstallRecord>>(File.ReadAllText(StatePath))
                ?? throw new InvalidDataException("설치 기록을 읽을 수 없습니다.")
            : new();
    }

    private void Save(Dictionary<string, InstallRecord> state)
    {
        var temporary = StatePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(state, Json));
        File.Move(temporary, StatePath, true);
    }

    public ToolStatus Status(ToolSpec tool)
    {
        var target = paths.Target(tool);
        var state = ReadState();
        if (!Directory.Exists(target)) return new(tool, target, "—", false, false, false, "설치 안 됨");
        if (!state.TryGetValue(tool.Id, out var record))
            return new(tool, target, "기존 설치", false, true, false, "외부 설치 — 덮어쓰기·삭제 안 함");
        ValidateRecord(tool, record);
        var modified = !SafeFiles.Same(record.Hashes, SafeFiles.Hashes(target));
        return new(tool, target, record.Version, true, false, modified,
            modified ? "로컬 변경 있음 — 변경 보존을 위해 작업 차단" : "관리 중");
    }

    private void ValidateRecord(ToolSpec tool, InstallRecord record)
    {
        if (record.Id != tool.Id || !Path.GetFullPath(record.Path).Equals(Path.GetFullPath(paths.Target(tool)), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("설치 기록의 경로가 현재 관리 경로와 다릅니다.");
        SafeFiles.NoLinks(record.Path);
    }

    private BackupInfo Backup(InstallRecord record)
    {
        var root = SafeFiles.Child(paths.Data, "Backups/" + record.Id);
        var directory = SafeFiles.Child(root, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff") + "-" + Guid.NewGuid().ToString("N"));
        SafeFiles.Copy(record.Path, Path.Combine(directory, "content"));
        File.WriteAllText(Path.Combine(directory, "record.json"), JsonSerializer.Serialize(record, Json));
        return new(directory, record);
    }

    public IReadOnlyList<BackupInfo> Backups(ToolSpec tool)
    {
        var root = SafeFiles.Child(paths.Data, "Backups/" + tool.Id);
        if (!Directory.Exists(root)) return [];
        var result = new List<BackupInfo>();
        foreach (var directory in Directory.GetDirectories(root).OrderDescending())
        {
            SafeFiles.NoLinks(directory);
            var file = Path.Combine(directory, "record.json");
            if (!File.Exists(file)) continue; // An interrupted backup is never restorable.
            var record = JsonSerializer.Deserialize<InstallRecord>(File.ReadAllText(file));
            if (record is null) continue;
            ValidateRecord(tool, record);
            result.Add(new(directory, record));
        }
        return result;
    }

    public void Install(ToolSpec tool, SourcePackage package)
    {
        using var operation = Lock();
        ensureHostClosed?.Invoke(tool);
        var target = paths.Target(tool);
        var state = ReadState();
        if (state.TryGetValue(tool.Id, out var old)) ValidateRecord(tool, old);
        if (Directory.Exists(target))
        {
            if (old is null) throw new InvalidOperationException("기존 설치 폴더를 덮어쓰지 않습니다. 기존 설치 연결을 사용하십시오.");
            EnsureUnchanged(old);
        }
        ValidatePackage(tool, package.Path);
        var parent = Path.GetDirectoryName(target)!;
        SafeFiles.NoLinks(parent);
        Directory.CreateDirectory(parent);
        var stage = SafeFiles.Child(parent, ".robim-stage-" + Guid.NewGuid().ToString("N"));
        var retired = SafeFiles.Child(parent, ".robim-old-" + Guid.NewGuid().ToString("N"));
        try
        {
            SafeFiles.Copy(package.Path, stage);
            var hashes = SafeFiles.Hashes(stage);
            ensureHostClosed?.Invoke(tool); // Recheck after download/copy.
            var hadOld = Directory.Exists(target);
            if (hadOld)
            {
                EnsureUnchanged(old!);
                Backup(old!);
                Directory.Move(target, retired);
            }
            try
            {
                Directory.Move(stage, target);
                state[tool.Id] = new(tool.Id, package.Version, target, DateTimeOffset.UtcNow, hashes);
                Save(state);
            }
            catch
            {
                if (Directory.Exists(target)) Directory.Move(target, stage);
                if (hadOld) Directory.Move(retired, target);
                throw;
            }
            // Successful state is durable before removing the retired copy.
            TryCleanup(parent, retired);
        }
        finally { TryCleanup(parent, stage); }
    }

    public void Remove(ToolSpec tool)
    {
        using var operation = Lock();
        ensureHostClosed?.Invoke(tool);
        var state = ReadState();
        if (!state.TryGetValue(tool.Id, out var record)) throw new InvalidOperationException("관리자가 설치한 도구만 제거할 수 있습니다.");
        ValidateRecord(tool, record);
        EnsureUnchanged(record);
        Backup(record);
        var target = paths.Target(tool);
        var parent = Path.GetDirectoryName(target)!;
        var retired = SafeFiles.Child(parent, ".robim-removed-" + Guid.NewGuid().ToString("N"));
        Directory.Move(target, retired);
        try { state.Remove(tool.Id); Save(state); }
        catch { Directory.Move(retired, target); throw; }
        TryCleanup(parent, retired);
    }

    public void Restore(ToolSpec tool, BackupInfo backup)
    {
        ValidateRecord(tool, backup.Record);
        var root = SafeFiles.Child(paths.Data, "Backups/" + tool.Id);
        var directory = SafeFiles.Child(root, Path.GetFileName(backup.Directory));
        if (!directory.Equals(Path.GetFullPath(backup.Directory), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("복원 경로가 관리 백업 폴더 밖에 있습니다.");
        var content = Path.Combine(directory, "content");
        if (!SafeFiles.Same(backup.Record.Hashes, SafeFiles.Hashes(content)))
            throw new InvalidDataException("백업 파일이 변경되어 복원하지 않습니다.");
        Install(tool, new(content, backup.Record.Version));
    }

    private static void EnsureUnchanged(InstallRecord record)
    {
        if (!Directory.Exists(record.Path) || !SafeFiles.Same(record.Hashes, SafeFiles.Hashes(record.Path)))
            throw new InvalidOperationException("설치 파일에 로컬 변경이 있습니다. 변경 파일을 별도로 보관한 뒤 다시 시도하십시오.");
    }

    public static void ValidatePackage(ToolSpec tool, string source)
    {
        SafeFiles.NoLinks(source);
        _ = SafeFiles.Hashes(source);
        if (tool.Kind == ToolKind.AutoCAD)
        {
            var manifest = System.Xml.Linq.XDocument.Load(Path.Combine(source, "PackageContents.xml"));
            if (tool.CustomPackage)
            {
                if (manifest.Root?.Name.LocalName != "ApplicationPackage") throw new InvalidDataException("AutoCAD bundle 매니페스트 오류");
                var modules = manifest.Descendants().Where(x => x.Name.LocalName == "ComponentEntry").Select(x => (string?)x.Attribute("ModuleName")).ToArray();
                if (modules.Length == 0) throw new InvalidDataException("AutoCAD 실행 모듈이 없습니다.");
                foreach (var module in modules)
                {
                    var relative = (module ?? "").Replace('\\', '/');
                    if (relative.StartsWith("./")) relative = relative[2..];
                    if (relative.Length == 0 || !File.Exists(SafeFiles.Child(source, relative))) throw new InvalidDataException("AutoCAD 실행 파일 누락 또는 경로 오류");
                }
            }
            else if (manifest.Root?.Name.LocalName != "ApplicationPackage" ||
                !File.Exists(Path.Combine(source, "Contents", "Windows", "DelHiddenNet.dll")))
                throw new InvalidDataException("AutoCAD 패키지 구조가 올바르지 않습니다.");
        }
        else if (!Directory.GetDirectories(source, "*.tab").Any() || (!tool.CustomPackage && !Directory.Exists(Path.Combine(source, "lib"))))
            throw new InvalidDataException("RoBIM pyRevit 패키지 구조가 올바르지 않습니다.");
    }

    private static void TryCleanup(string parent, string directory)
    {
        try { SafeFiles.DeleteOwnedTree(parent, directory); }
        catch (IOException) { /* Keep a recoverable staging copy if a file is locked. */ }
        catch (UnauthorizedAccessException) { }
    }
}
