using System.Diagnostics;
using RoBIM.Cad.Configuration;

namespace RoBIM.Manager.Core;

public sealed record CadSession(int Pid, long StartTicks, string Title);

// The desktop only prepares immutable payloads. Applying them requires ROBIMRELOAD inside CAD.
public sealed class CadReload(ManagerPaths paths)
{
    public string Data => SafeFiles.Child(paths.Data, "Cad");
    public static string WindowsDirectory(string bundle) => SafeFiles.Child(bundle, "Contents/Windows");
    public static bool Compatible(string bundle) => File.Exists(Path.Combine(bundle, "Contents", "Windows", CadFiles.ManifestName)) &&
        File.Exists(Path.Combine(bundle, "Contents", "Windows", "RoBIM.Cad.Loader.dll"));
    public CadButtonSource? Source(string installedBundle, SourcePackage? available = null, bool preferInstalled = false)
    {
        if (!preferInstalled && available is not null)
        {
            var root = WindowsDirectory(available.Path); return new(root, available.Path, CadFiles.Catalog(root), null);
        }
        if (!Compatible(installedBundle)) return null;
        var desired = Desired(installedBundle);
        if (desired is not null)
        {
            var root = CadFiles.Snapshot(Data, desired); return new(root, installedBundle, CadFiles.Catalog(root), desired);
        }
        var installed = WindowsDirectory(installedBundle); return new(installed, installedBundle, CadFiles.Catalog(installed), null);
    }
    public CadRequest Toggle(ToolSpec tool, CadButtonSource source, string key, bool enabled, CadSession? session)
    {
        var status = new InstallService(paths).Status(tool);
        if (source.NeedsConnection(status)) throw new IOException("CAD 연결 모듈 변경은 AutoCAD 종료 후 설치하십시오.");
        if (!source.Catalog.Modules.Any(m => m.Actions.Any(a => CadFiles.Key(m, a) == key))) throw new IOException("존재하지 않는 CAD 버튼입니다. 목록을 새로 고치십시오.");
        var ids = Desired(status.Path)?.SelectedActions.ToHashSet() ?? [];
        if (enabled) ids.Add(key); else ids.Remove(key);
        return Prepare(tool, source.Bundle, source.Catalog, ids.ToArray(), session, source.Previous);
    }
    public CadButtonSource? SourceForAction(string installedBundle, string key, SourcePackage? available = null)
    {
        var installed = Source(installedBundle, preferInstalled: true);
        if (installed?.Catalog.Modules.Any(m => m.Actions.Any(a => CadFiles.Key(m, a) == key)) == true) return installed;
        return Source(installedBundle, available);
    }
    public static List<CadSession> Sessions()
    {
        var result = new List<CadSession>();
        foreach (var p in Process.GetProcessesByName("acad"))
        {
            using (p)
            {
                try { result.Add(new(p.Id, p.StartTime.ToUniversalTime().Ticks, string.IsNullOrWhiteSpace(p.MainWindowTitle) ? "AutoCAD" : p.MainWindowTitle)); }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            }
        }
        return result;
    }
    public CadRequest? Desired(string bundle)
    {
        var file = Path.Combine(Data, "pending.json");
        if (!File.Exists(file)) file = Path.Combine(Data, "active.json");
        if (!File.Exists(file)) return null;
        try
        {
            var request = CadFiles.Read<CadRequest>(file);
            return Path.GetFullPath(request.BundlePath).Equals(Path.GetFullPath(bundle), StringComparison.OrdinalIgnoreCase) ? request : null;
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or ArgumentException)
        { return null; } // New preparation can replace an unreadable pending record.
    }
    public string Status(string bundle)
    {
        try
        {
            var desired = Desired(bundle);
            if (desired is null) return "버튼을 선택한 뒤 적용 준비를 누르십시오.";
            if (!CadFiles.Hash(Path.Combine(WindowsDirectory(bundle), "RoBIM.Cad.Loader.dll")).Equals(desired.LoaderHash, StringComparison.OrdinalIgnoreCase))
                return "연결 모듈이 변경됐습니다. 버튼 선택을 다시 준비하십시오.";
            var resultFile = CadFiles.Child(Data, "Results/" + desired.Id + ".json");
            if (!File.Exists(resultFile)) return "준비됨 — AutoCAD의 RoBIM → 관리 → Reload를 누르십시오.";
            var result = CadFiles.Read<CadReceipt>(resultFile);
            return result.Status == "applied" ? $"CAD 적용 완료 · 버튼 {desired.SelectedActions.Count}개 · PID {result.Pid}" : "CAD 적용 실패: " + result.Message;
        }
        catch (Exception ex) { return "CAD 준비 상태 확인 필요: " + ex.Message; }
    }
    public CadRequest Prepare(ToolSpec tool, string sourceBundle, CadCatalog catalog, IReadOnlyList<string> selected, CadSession? session, CadRequest? previousSource = null)
    {
        if (tool.Kind != ToolKind.AutoCAD) throw new InvalidOperationException("AutoCAD 패키지만 준비할 수 있습니다.");
        var status = new InstallService(paths).Status(tool);
        if (!status.Managed || status.Modified || status.External) throw new IOException("정상 관리 중인 CAD 연결 모듈을 먼저 설치하십시오. 로컬 변경은 덮어쓰지 않습니다.");
        if (!Compatible(status.Path) || (previousSource is null && !Compatible(sourceBundle))) throw new IOException("새 CAD 연결 모듈을 AutoCAD 종료 후 먼저 설치하십시오.");
        var installed = WindowsDirectory(status.Path);
        var source = previousSource is null ? WindowsDirectory(sourceBundle) : CadFiles.Snapshot(Data, previousSource);
        var coreHash = previousSource?.LoaderHash ?? CadFiles.Hash(CadFiles.Child(source, "RoBIM.Cad.Loader.dll"));
        if (previousSource is not null && !Path.GetFullPath(previousSource.BundlePath).Equals(Path.GetFullPath(status.Path), StringComparison.OrdinalIgnoreCase)) throw new IOException("다른 CAD 설치의 준비 파일입니다.");
        if (CadFiles.Hash(CadFiles.Child(installed, "RoBIM.Cad.Loader.dll")) != coreHash)
            throw new IOException("CAD 연결 모듈이 변경됐습니다. AutoCAD 종료 후 전체 업데이트가 필요합니다.");
        CadFiles.Validate(source, catalog);
        var known = catalog.Modules.SelectMany(m => m.Actions.Select(a => CadFiles.Key(m, a))).ToHashSet(StringComparer.Ordinal);
        if (selected.Count != selected.Distinct().Count() || selected.Any(k => !known.Contains(k))) throw new InvalidDataException("CAD 버튼 선택 오류");
        if (session is not null && !Sessions().Any(s => s.Pid == session.Pid && s.StartTicks == session.StartTicks))
            throw new IOException("선택한 AutoCAD가 종료되거나 다시 시작됐습니다. 세션을 다시 선택하십시오.");
        CadFiles.NoLinks(Data); Directory.CreateDirectory(Data);
        using var operation = new FileStream(Path.Combine(Data, "operation.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var id = Guid.NewGuid().ToString("N");
        var snapshot = CadFiles.Child(Data, "Snapshots/" + id);
        Directory.CreateDirectory(snapshot);
        foreach (var relative in CadFiles.PayloadFiles(catalog).Where(f => f != CadFiles.ManifestName))
        {
            var target = CadFiles.Child(snapshot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(CadFiles.Child(source, relative), target, false);
        }
        CadFiles.Save(CadFiles.Child(snapshot, CadFiles.ManifestName), catalog);
        var request = new CadRequest
        {
            Id = id, PreparedAt = DateTimeOffset.UtcNow, BundlePath = Path.GetFullPath(status.Path),
            LoaderHash = coreHash,
            TargetPid = session?.Pid ?? 0, TargetStartTicks = session?.StartTicks ?? 0,
            SelectedActions = selected.ToList(),
            Hashes = CadFiles.PayloadFiles(catalog).ToDictionary(f => f, f => CadFiles.Hash(CadFiles.Child(snapshot, f)), StringComparer.OrdinalIgnoreCase)
        };
        _ = CadFiles.Snapshot(Data, request);
        CadFiles.Save(Path.Combine(Data, "pending.json"), request);
        return request;
    }
}
