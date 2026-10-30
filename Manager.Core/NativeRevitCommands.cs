using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;
using RoBIM.Revit.Reload;

namespace RoBIM.Manager.Core;

public sealed record RevitSession(int Pid, long StartTicks);
public sealed record NativeCatalogPackage(string Path, string Version, Dictionary<string, string> Hashes);
public sealed class NativeRevitCommands(ManagerPaths paths, Func<RevitSession?>? sessionProbe = null, Action<string>? requireChannel = null, Func<IReadOnlyList<RevitSession>>? sessionsProbe = null)
{
    public void ToggleWithMigration(ToolSpec tool, string source, string commandId, bool selected)
    {
        if (selected)
        {
            var module = NativeProtocol.Module(source);
            requireChannel?.Invoke(ModuleReleases.Native(source, module.Id, module.Version).Channel);
        }
        if (tool.Kind != ToolKind.NativeRevit) throw new IOException("Revit 모듈만 전환할 수 있습니다.");
        var installer = new InstallService(paths, EnvironmentProbe.EnsureHostClosed);
        var legacy = installer.Status(tool);
        if (legacy.External)
        {
            EnvironmentProbe.EnsureHostClosed(tool);
            var reference = new NativeLegacyPackages(paths).VerifiedReference(tool) ??
                throw new IOException("기존 외부 설치와 일치하는 확인된 구형 배포본이 없습니다. 기존 파일은 변경하지 않습니다.\n" + legacy.Path);
            installer.AdoptVerifiedNative(tool, reference); legacy = installer.Status(tool);
        }
        BackupInfo? backup = null;
        if (legacy.Managed)
        {
            EnvironmentProbe.EnsureHostClosed(tool); installer.Remove(tool); backup = installer.Backups(tool).First();
        }
        try { Toggle(source, commandId, selected); }
        catch { if (backup is not null) installer.Restore(tool, backup); throw; }
    }
    private IReadOnlyList<RevitSession> CurrentSessions() => sessionsProbe is not null ? sessionsProbe() : sessionProbe is not null ? sessionProbe() is { } one ? [one] : [] : Sessions();
    private RevitSession? CurrentSession() { var sessions=CurrentSessions(); return sessions.Count==0?null:sessions.Count==1?sessions[0]:new(0,0); }
    public string Data => SafeFiles.Child(paths.Data, "NativeRevit");
    public static RevitSession? Session() { var sessions=Sessions(); return sessions.Count==0?null:sessions.Count==1?sessions[0]:new(0,0); }
    public static IReadOnlyList<RevitSession> Sessions()
    {
        var result=new List<RevitSession>();
        foreach(var process in Process.GetProcessesByName("Revit"))
        {
            using(process) { try {
                var executable=process.MainModule?.FileName; if(executable is null) continue;
                var version=FileVersionInfo.GetVersionInfo(executable);
                if(version.FileMajorPart==26 && version.FileMinorPart>=5) result.Add(new(process.Id,process.StartTime.ToUniversalTime().Ticks));
            } catch(Exception error) when(error is System.ComponentModel.Win32Exception or InvalidOperationException) { } }
        }
        return result;
    }
    public NativeRequest? Desired()
    {
        var pending = NativeProtocol.Child(Data, "pending.json"); var active = NativeProtocol.Child(Data, "active.json");
        return File.Exists(pending) ? NativeProtocol.Read<NativeRequest>(pending) : File.Exists(active) ? NativeProtocol.Read<NativeRequest>(active) : null;
    }
    public NativeRequest Toggle(string package, string commandId, bool selected)
    {
        var manifest = NativeProtocol.Module(package); var current = Desired();
        if (selected) requireChannel?.Invoke(ModuleReleases.Native(package, manifest.Id, manifest.Version).Channel);
        if (!selected)
        {
            // Removal preserves the selected generation, even if a newer TEST catalog was imported.
            var installed = current?.Modules.FirstOrDefault(x => x.Manifest.Id == manifest.Id);
            var remaining = installed?.Selected.Where(id => id != commandId).ToArray() ?? [];
            var removalSession = CurrentSession();
            return NativeProtocol.Prepare(Data, installed?.Directory, manifest.Id, remaining,
                removalSession?.Pid ?? 0, removalSession?.StartTicks ?? 0);
        }
        var cacheFile = NativeProtocol.Child(Data, "catalog-" + manifest.Id + ".json");
        var cache = NativeProtocol.Read<NativeCatalogPackage>(cacheFile);
        if (!Path.GetFullPath(cache.Path).Equals(Path.GetFullPath(package), StringComparison.OrdinalIgnoreCase) || !NativeProtocol.Same(cache.Hashes, NativeProtocol.Hashes(package))) throw new IOException("Revit 기능 원본 캐시가 변경되어 적용하지 않습니다. 패키지를 다시 가져오십시오.");
        var ids = current?.Modules.FirstOrDefault(x => x.Manifest.Id == manifest.Id)?.Selected.ToHashSet() ?? [];
        if (selected) ids.Add(commandId); else ids.Remove(commandId);
        var session = CurrentSession(); return NativeProtocol.Prepare(Data, package, manifest.Id, ids.ToArray(), session?.Pid ?? 0, session?.StartTicks ?? 0);
    }
    public void RequestReload(bool includeCachedUpdates = true)
    {
        if (includeCachedUpdates) PrepareCachedUpdates();
        var pending = NativeProtocol.Child(Data, "pending.json");
        if (!File.Exists(pending)) throw new IOException("먼저 설치·제거 또는 변경할 기능을 준비하십시오.");
        var request = NativeProtocol.Read<NativeRequest>(pending); NativeProtocol.Validate(Data, request);
        var sessions=CurrentSessions();
        var session=sessions.Count==1?sessions[0]:sessions.Count>1?new RevitSession(0,0):throw new IOException("Revit을 실행하면 준비한 기능을 시작 시 적용합니다.");
        if(request.TargetPid!=0 && !sessions.Any(s=>s.Pid==request.TargetPid && s.StartTicks==request.TargetStartTicks)) EnsureTarget(request,session);
        var outgoing=request with {TargetPid=session.Pid,TargetStartTicks=session.StartTicks,Targets=sessions.Count>1?sessions.Select(s=>new NativeTarget(s.Pid,s.StartTicks)).ToArray():null,ApplyRequested=true};
        NativeProtocol.Validate(Data,outgoing);NativeProtocol.Save(pending,outgoing);
    }
    public bool HasUpdate(string moduleId)
    {
        var entry = Desired()?.Modules.FirstOrDefault(x => x.Manifest.Id == moduleId); var file = NativeProtocol.Child(Data, "catalog-" + moduleId + ".json");
        return entry is not null && File.Exists(file) && !NativeProtocol.Same(entry.Hashes, NativeProtocol.Read<NativeCatalogPackage>(file).Hashes);
    }
    public void PrepareCachedUpdates()
    {
        var desired = Desired(); if (desired is null) return;
        var currentSession = CurrentSession();
        if (currentSession is not null && currentSession.Pid!=0) EnsureTarget(desired, currentSession);
        NativeRequest? prepared = null;
        foreach (var entry in desired.Modules)
        {
            if (!HasUpdate(entry.Manifest.Id)) continue;
            var package = Cached(entry.Manifest.Id)!;
            if (requireChannel is not null)
            {
                try { requireChannel(ModuleReleases.Native(package.Path, entry.Manifest.Id, NativeProtocol.Module(package.Path).Version).Channel); }
                catch (IOException) { continue; }
            }
            Cache(package);
            var module = NativeProtocol.Module(package.Path);
            // Importing a catalog is not consent to change a selected release channel.
            if (ModuleReleases.Native(package.Path, module.Id, module.Version).Channel !=
                ModuleReleases.Native(entry.Directory, entry.Manifest.Id, entry.Manifest.Version).Channel) continue;
            if (entry.Selected.Any(id => !module.Commands.Any(c => c.Id == id))) throw new IOException("새 패키지에서 제거된 버튼이 있습니다. 기존 선택을 검토한 뒤 다시 준비하십시오.");
            prepared = NativeProtocol.Prepare(Data, package.Path, module.Id, entry.Selected, currentSession?.Pid ?? 0, currentSession?.StartTicks ?? 0, prepared ?? desired, false);
        }
        if (prepared is not null) NativeProtocol.Publish(Data, prepared, desired.Id);
    }
    private NativeReceipt? Receipt(NativeRequest request, RevitSession session)
    {
        var file = NativeProtocol.Child(Data, $"Results/{request.Id}-{session.Pid}-{session.StartTicks}.json");
        if (!File.Exists(file)) return null;
        var value = NativeProtocol.Read<NativeReceipt>(file);
        return value.RequestId == request.Id && value.Pid == session.Pid && value.StartTicks == session.StartTicks ? value : null;
    }
    private void EnsureTarget(NativeRequest request, RevitSession session)
    {
        if (request.TargetPid != 0 && (request.TargetPid != session.Pid || request.TargetStartTicks != session.StartTicks) && Receipt(request, session)?.Status != "applied") throw new IOException("준비했던 Revit 세션이 바뀌었습니다. 기능을 다시 준비하십시오.");
    }
    public (bool Selected, bool Pending, string Message) State(string moduleId, string commandId)
    {
        var desired = Desired(); if (desired is null) return (false, false, "설치 안 됨");
        var selected = desired.Modules.Any(m => m.Manifest.Id == moduleId && m.Selected.Contains(commandId));
        var sessions=CurrentSessions();
        if(sessions.Count>1) {
            var results=sessions.Select(s=>{
                try {var receipt=Receipt(desired,s);return new SessionApplyResult(s.Pid,s.StartTicks,receipt?.Status??"pending",receipt?.Message);}
                catch(Exception error) when(error is IOException or JsonException){return new SessionApplyResult(s.Pid,s.StartTicks,"failed",error.Message);}
            }).ToArray();
            return(selected,results.Any(r=>r.Status!="applied"),SessionApplyResult.Summary("Revit",results));
        }
        RevitSession? session=sessions.SingleOrDefault();
        var applied = session is null ? null : Receipt(desired, session);
        if (applied?.Status == "failed") return (selected, true, "Reload 실패: " + applied.Message);
        if (applied?.Status == "applied" && applied.RequestId == desired.Id) return (selected, false, selected ? "설치됨 · Reload 완료" : "제거됨 · Reload 완료");
        var activePath = NativeProtocol.Child(Data, "active.json");
        var active = File.Exists(activePath) ? NativeProtocol.Read<NativeRequest>(activePath) : null;
        if (session is null && active?.Id == desired.Id) return (selected, false, selected ? "설치됨 · Revit 시작 시 로드" : "설치 안 됨");
        var wasSelected = active?.Modules.Any(m => m.Manifest.Id == moduleId && m.Selected.Contains(commandId)) == true;
        var next = session is null ? "Revit 시작 시 반영" : desired.ApplyRequested ? "자동 적용 대기 · Revit 명령/창 종료 후 반영" : "Reload 필요";
        return (selected, selected || wasSelected, selected ? "설치 준비됨 · " + next : wasSelected ? "제거 준비됨 · " + next : "설치 안 됨");
    }
    public SourcePackage Import(string archive)
    {
        var digest = archive + ".sha256";
        if (!File.Exists(digest) || !SafeFiles.Hash(archive).Equals(File.ReadAllText(digest).Trim(), StringComparison.OrdinalIgnoreCase)) throw new IOException("Revit 패키지 SHA-256 검증 실패");
        var cache = SafeFiles.Child(Data, "Catalog/" + Guid.NewGuid().ToString("N")); SafeFiles.Extract(archive, cache);
        var roots = Directory.GetDirectories(cache); if (roots.Length != 1) throw new IOException("Revit 패키지 루트 오류");
        var manifest = NativeProtocol.Module(roots[0]);
        ModuleReleases.ValidateNative(roots[0]);
        requireChannel?.Invoke(ModuleReleases.Native(roots[0], manifest.Id, manifest.Version).Channel);
        var package = new SourcePackage(roots[0], manifest.Version);
        NativeProtocol.Save(NativeProtocol.Child(Data, "catalog-" + manifest.Id + ".json"), new NativeCatalogPackage(package.Path, package.Version, NativeProtocol.Hashes(package.Path)));
        PrepareCachedUpdates(); return package;
    }
    public SourcePackage? Cached(string moduleId)
    {
        var file = NativeProtocol.Child(Data, "catalog-" + moduleId + ".json"); if (!File.Exists(file)) return null;
        var stored = NativeProtocol.Read<NativeCatalogPackage>(file);
        var package = new SourcePackage(stored.Path, stored.Version);
        var prefix = Path.GetFullPath(NativeProtocol.Child(Data, "Catalog")) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(package.Path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || NativeProtocol.Module(package.Path).Id != moduleId) throw new IOException("Revit 목록 캐시 경로 오류");
        return package;
    }
    public SourcePackage Cache(SourcePackage source)
    {
        var module = NativeProtocol.Module(source.Path);
        ModuleReleases.ValidateNative(source.Path);
        requireChannel?.Invoke(ModuleReleases.Native(source.Path, module.Id, module.Version).Channel);
        var current = Cached(module.Id);
        if (current is not null && Path.GetFullPath(current.Path).Equals(Path.GetFullPath(source.Path), StringComparison.OrdinalIgnoreCase))
        {
            var record = NativeProtocol.Read<NativeCatalogPackage>(NativeProtocol.Child(Data, "catalog-" + module.Id + ".json"));
            if (!NativeProtocol.Same(record.Hashes, NativeProtocol.Hashes(current.Path))) throw new IOException("Revit 기능 원본 캐시의 변경을 보존하기 위해 중단합니다.");
            return current;
        }
        var target = NativeProtocol.Child(Data, "Catalog/" + Guid.NewGuid().ToString("N") + "/" + Path.GetFileName(source.Path));
        var before = NativeProtocol.Hashes(source.Path); NativeProtocol.Copy(source.Path, target);
        if (!NativeProtocol.Same(before, NativeProtocol.Hashes(source.Path)) || !NativeProtocol.Same(before, NativeProtocol.Hashes(target))) throw new IOException("Revit 패키지 복사 중 변경 감지");
        NativeProtocol.Save(NativeProtocol.Child(Data, "catalog-" + module.Id + ".json"), new NativeCatalogPackage(target, source.Version, before));
        return new(target, source.Version);
    }
    public SourcePackage? CachedForFolder(string folder)
    {
        if (!Directory.Exists(Data)) return null;
        foreach (var file in Directory.GetFiles(Data, "catalog-*.json"))
        {
            var moduleId = Path.GetFileNameWithoutExtension(file)[8..];
            if (!NativeProtocol.Id(moduleId)) continue;
            var package = Cached(moduleId);
            if (package is not null && Path.GetFileName(package.Path).Equals(folder, StringComparison.OrdinalIgnoreCase)) return package;
        }
        return null;
    }
    public IEnumerable<SourcePackage> CachedPackages()
    {
        if (!Directory.Exists(Data)) yield break;
        foreach (var file in Directory.GetFiles(Data, "catalog-*.json"))
        {
            var id = Path.GetFileNameWithoutExtension(file)[8..];
            if (NativeProtocol.Id(id) && Cached(id) is { } package) yield return package;
        }
    }
    public void InstallHost(string archive, string addinDirectory, Action? ensureHostClosed = null)
    {
        // A virtualized LocalAppData target can exist here but be missing in normally launched Revit.
        WindowsDataPath.EnsureDirectory(paths.Data);
        var digest = archive + ".sha256";
        if (!File.Exists(digest) || !SafeFiles.Hash(archive).Equals(File.ReadAllText(digest).Trim(), StringComparison.OrdinalIgnoreCase)) throw new IOException("Revit 로더 패키지 SHA-256 검증 실패");
        var root = SafeFiles.Child(Data, "HostDownloads/" + Guid.NewGuid().ToString("N")); SafeFiles.Extract(archive, root);
        var package = SafeFiles.Child(root, Catalog.RevitHost.FolderName); InstallService.ValidatePackage(Catalog.RevitHost, package);
        var target = paths.Target(Catalog.RevitHost);
        var installer = new InstallService(paths, _ => { if (Directory.Exists(target)) { if (ensureHostClosed is null) EnvironmentProbe.EnsureHostClosed(Catalog.RevitHost); else ensureHostClosed(); } });
        var status = installer.Status(Catalog.RevitHost);
        if (status.Modified || status.External) throw new IOException("Revit 로더의 로컬 변경 또는 외부 설치를 보존하기 위해 중단합니다.");
        var addin = SafeFiles.Child(addinDirectory, "RoBIM.NativeHost.addin");
        var xml = XDocument.Load(Path.Combine(package, "Contents", "2026", "RoBIM.Revit.Host.addin"));
        xml.Root!.Element("AddIn")!.Element("Assembly")!.Value = SafeFiles.Child(target, "Contents/2026/RoBIM.Revit.Host.dll");
        var generated = xml.ToString();
        SafeFiles.NoLinks(addin);
        if (File.Exists(addin) && File.ReadAllText(addin) != generated) throw new IOException("같은 이름의 Revit 등록 파일이 있어 보존합니다: " + addin);
        if (!status.Managed || !SafeFiles.Same(SafeFiles.Hashes(package), SafeFiles.Hashes(target))) installer.Install(Catalog.RevitHost, new(package, FileVersionInfo.GetVersionInfo(Path.Combine(package, "Contents", "2026", "RoBIM.Revit.Host.dll")).ProductVersion?.Split('+')[0] is { Length: > 0 } version ? version : "1.0.0-preview.1"));
        Directory.CreateDirectory(addinDirectory);
        if (!File.Exists(addin)) { using var stream = new FileStream(addin, FileMode.CreateNew, FileAccess.Write, FileShare.None); using var writer = new StreamWriter(stream); writer.Write(generated); }
    }
}
