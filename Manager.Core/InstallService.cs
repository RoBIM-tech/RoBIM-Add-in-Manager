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

    private void ValidateRecord(ToolSpec tool, InstallRecord record, bool backup = false)
    {
        if (record.Id != tool.Id || (!Path.GetFullPath(record.Path).Equals(Path.GetFullPath(paths.Target(tool)), StringComparison.OrdinalIgnoreCase) &&
            !(backup && tool.Kind == ToolKind.PyRevit && PyRevitLocation.HistoricalTarget(paths, tool, record.Path))))
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
            ValidateRecord(tool, record, true);
            result.Add(new(directory, record));
        }
        return result;
    }

    public void Install(ToolSpec tool, SourcePackage package)
    {
        RevitRuntimePolicy.EnsureAllowed(paths, tool);
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

    // A running pyRevit engine can hold its extension directory open. Keep that root
    // and identical binaries in place; replace only the verified reloadable file delta.
    public void InstallReload(ToolSpec tool, SourcePackage package)
    {
        RevitRuntimePolicy.EnsureAllowed(paths, tool);
        if (tool.Kind != ToolKind.PyRevit) throw new IOException("pyRevit 파일만 실행 중 교체할 수 있습니다.");
        using var operation = Lock();
        ensureHostClosed?.Invoke(tool);
        var state = ReadState();
        if (!state.TryGetValue(tool.Id, out var old)) throw new IOException("관리 중인 설치만 Reload할 수 있습니다.");
        ValidateRecord(tool, old); EnsureUnchanged(old); ValidatePackage(tool, package.Path);
        var stage = SafeFiles.Child(paths.Data, "ReloadApply/" + Guid.NewGuid().ToString("N"));
        var completed = new List<string>();
        BackupInfo? backup = null;
        var target = paths.Target(tool);
        void Put(string source, string destination)
        {
            SafeFiles.NoLinks(destination);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".reloadtmp";
            try { File.Copy(source, temporary); File.Move(temporary, destination, true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        try
        {
            SafeFiles.Copy(package.Path, stage, false);
            var after = SafeFiles.Hashes(stage);
            RevitReload.EnsureReloadable(old.Hashes, after);
            ensureHostClosed?.Invoke(tool); EnsureUnchanged(old);
            backup = Backup(old);
            foreach (var relative in old.Hashes.Keys.Union(after.Keys, StringComparer.OrdinalIgnoreCase))
            {
                if (old.Hashes.GetValueOrDefault(relative) == after.GetValueOrDefault(relative)) continue;
                var destination = SafeFiles.Child(target, relative);
                var expected = old.Hashes.GetValueOrDefault(relative);
                if ((File.Exists(destination) ? SafeFiles.Hash(destination) : null) != expected)
                    throw new IOException("Reload 중 설치 파일이 변경되었습니다: " + relative);
                if (after.ContainsKey(relative)) Put(SafeFiles.Child(stage, relative), destination);
                else File.Delete(destination);
                completed.Add(relative);
            }
            if (!SafeFiles.Same(after, SafeFiles.Hashes(target))) throw new IOException("Reload 파일 검증 실패");
            state[tool.Id] = new(tool.Id, package.Version, target, DateTimeOffset.UtcNow, after);
            Save(state);
        }
        catch (Exception update)
        {
            if (backup is not null)
            {
                try
                {
                    foreach (var relative in completed.AsEnumerable().Reverse())
                    {
                        var destination = SafeFiles.Child(target, relative);
                        if (old.Hashes.TryGetValue(relative, out var hash))
                        {
                            var original = SafeFiles.Child(Path.Combine(backup.Directory, "content"), relative);
                            if (SafeFiles.Hash(original) != hash) throw new IOException("Reload 복구 백업 변경 감지");
                            Put(original, destination);
                        }
                        else if (File.Exists(destination)) File.Delete(destination);
                    }
                    if (!SafeFiles.Same(old.Hashes, SafeFiles.Hashes(target))) throw new IOException("Reload 복구 검증 실패");
                }
                catch (Exception restore) { throw new IOException("Reload와 복구가 실패했습니다. 백업을 확인하십시오.", new AggregateException(update, restore)); }
            }
            throw;
        }
        finally { TryCleanup(Path.GetDirectoryName(stage)!, stage); }
        // Empty command bundle folders are also removed; a directory held by the host
        // may remain empty until exit, but no unselected command files remain in it.
        foreach (var directory in Directory.GetDirectories(target, "*", SearchOption.AllDirectories).OrderByDescending(x => x.Length))
            try { SafeFiles.NoLinks(directory); if (Path.GetDirectoryName(directory) != target && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory); }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    // Explicit handover only: caller supplies a verified distribution reference. Merely
    // recognizing a folder name is never enough to adopt or remove an external add-in.
    public void AdoptVerifiedNative(ToolSpec tool, SourcePackage reference)
    {
        using var operation = Lock();
        if (tool.Kind != ToolKind.NativeRevit) throw new IOException("독립 Revit 패키지만 검증 후 연결할 수 있습니다.");
        void Check() { if (ensureHostClosed is null) EnvironmentProbe.EnsureHostClosed(tool); else ensureHostClosed(tool); }
        Check();
        var target = paths.Target(tool); var state = ReadState();
        if (state.ContainsKey(tool.Id)) throw new IOException("이미 관리 중인 패키지입니다.");
        if (Path.GetFullPath(target).Equals(Path.GetFullPath(reference.Path), StringComparison.OrdinalIgnoreCase)) throw new IOException("별도 배포 원본이 필요합니다.");
        ValidatePackage(tool, reference.Path); ValidatePackage(tool, target);
        var expected = SafeFiles.Hashes(reference.Path);
        if (!SafeFiles.Same(expected, SafeFiles.Hashes(target))) throw new IOException("기존 설치가 확인된 배포본과 다릅니다. 사용자 변경을 보존합니다.");
        Check();
        state[tool.Id] = new(tool.Id, reference.Version, target, DateTimeOffset.UtcNow, expected); Save(state);
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
        ensureHostClosed?.Invoke(tool);
        EnsureUnchanged(record);
        Directory.Move(target, retired);
        try { state.Remove(tool.Id); Save(state); }
        catch { Directory.Move(retired, target); throw; }
        TryCleanup(parent, retired);
    }

    public void Restore(ToolSpec tool, BackupInfo backup)
    {
        ValidateRecord(tool, backup.Record, true);
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
        if (tool.Kind is ToolKind.NativeRevit or ToolKind.NativeRevitHost)
        {
            NativeRevitPackages.Validate(source);
        }
        else if (tool.Kind == ToolKind.AutoCAD)
        {
            var manifest = System.Xml.Linq.XDocument.Load(Path.Combine(source, "PackageContents.xml"));
            if (tool.CustomPackage || CadReload.Compatible(source))
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
                if (CadReload.Compatible(source))
                {
                    var root = CadReload.WindowsDirectory(source);
                    foreach (var module in RoBIM.Cad.Configuration.CadFiles.Catalog(root).Modules)
                        _ = ModuleReleases.Cad(root, module.Id, module.Version);
                }
            }
            else if (manifest.Root?.Name.LocalName != "ApplicationPackage" ||
                !File.Exists(Path.Combine(source, "Contents", "Windows", "DelHiddenNet.dll")))
                throw new InvalidDataException("AutoCAD 패키지 구조가 올바르지 않습니다.");
        }
        else if ((!Directory.GetDirectories(source, "*.tab").Any() && !(tool.Id == Catalog.RevitSelected.Id && RevitButtons.ValidEmptySelection(source))) || (!tool.CustomPackage && !Directory.Exists(Path.Combine(source, "lib"))))
            throw new InvalidDataException("RoBIM pyRevit 패키지 구조가 올바르지 않습니다.");
    }

    private static void TryCleanup(string parent, string directory)
    {
        try { SafeFiles.DeleteOwnedTree(parent, directory); }
        catch (IOException) { /* Keep a recoverable staging copy if a file is locked. */ }
        catch (UnauthorizedAccessException) { }
    }
}
