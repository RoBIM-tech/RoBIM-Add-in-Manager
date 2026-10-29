using RoBIM.Cad.Configuration;

namespace RoBIM.Manager.Core;

// Feature packages are inert payloads. Only the fixed loader is registered with AutoCAD.
public sealed class CadFeaturePackages(ManagerPaths paths)
{
    public static bool IsFeature(ExtensionPackage? package) => package is { Host: "autocad", ModuleId.Length: > 0, CommandId.Length: > 0 };
    public static CadModule Validate(string bundle, ExtensionPackage package)
    {
        if (!IsFeature(package)) throw new IOException("독립 CAD 버튼 표시 정보가 필요합니다.");
        _ = SafeFiles.Hashes(bundle);
        if (Directory.EnumerateFiles(bundle, "RoBIM.Cad.Loader.dll", SearchOption.AllDirectories).Any() || File.Exists(Path.Combine(bundle, "PackageContents.xml")))
            throw new IOException("기능 패키지에 고정 로더나 직접 로드 등록을 포함할 수 없습니다.");
        var root = CadReload.WindowsDirectory(bundle);
        var catalog = CadFiles.Catalog(root);
        if (catalog.Modules.Count != 1) throw new IOException("CAD 버튼 패키지는 모듈 하나만 제공해야 합니다.");
        var module = catalog.Modules.Single();
        if (module.Id != package.ModuleId || module.Actions.Count != 1 || module.Actions[0].Id != package.CommandId)
            throw new IOException("CAD 모듈·명령 ID가 등록과 다릅니다.");
        if (ModuleReleases.Cad(root, module.Id, module.Version).Channel != package.Channel)
            throw new IOException("CAD 실제 배포 채널이 선택과 다릅니다.");
        return module;
    }
    private string Index => SafeFiles.Child(paths.Data, "Cad/features.json");
    public SourcePackage? Cached(ExtensionPackage package)
    {
        if (!File.Exists(Index)) return null;
        var entries = CadFiles.Read<Dictionary<string, SourcePackage>>(Index);
        var source = entries.GetValueOrDefault(package.ModuleId);
        if (source is null) return null;
        // Cache channel may differ from the current registration after promotion.
        var catalog = CadFiles.Catalog(CadReload.WindowsDirectory(source.Path));
        if (catalog.Modules.Count != 1 || catalog.Modules[0].Id != package.ModuleId) throw new IOException("CAD 캐시 ID 오류");
        return source;
    }
    public void Cache(ExtensionPackage package, SourcePackage source)
    {
        var module = Validate(source.Path, package);
        if (source.Version.TrimStart('v', 'V') != module.Version) throw new IOException("CAD Release와 모듈 버전이 다릅니다.");
        var entries = File.Exists(Index) ? CadFiles.Read<Dictionary<string, SourcePackage>>(Index) : [];
        entries[package.ModuleId] = source; CadFiles.Save(Index, entries);
    }
    public CadRequest Set(ExtensionPackage package, SourcePackage? input, bool enabled, CadSession? session)
    {
        var status = new InstallService(paths).Status(Catalog.Cad);
        if (!status.Managed || status.Modified || status.External) throw new IOException("프로그램 로더에서 정상 CAD 로더를 먼저 설치하십시오.");
        var bridge = new CadReload(paths);
        var previous = bridge.Source(status.Path, preferInstalled: true) ?? throw new IOException("CAD 로더 구성을 읽지 못했습니다.");
        var staging = SafeFiles.Child(paths.Data, "Cad/FeatureStages/" + Guid.NewGuid().ToString("N") + "/" + Catalog.Cad.FolderName);
        SafeFiles.Copy(status.Path, staging, false);
        var root = CadReload.WindowsDirectory(staging);
        foreach (var relative in CadFiles.PayloadFiles(previous.Catalog)) Copy(previous.Root, root, relative);
        var modules = previous.Catalog.Modules.ToList();
        if (enabled)
        {
            if (input is null) throw new IOException("검증된 CAD 기능 패키지가 필요합니다.");
            var module = Validate(input.Path, package);
            var source = CadReload.WindowsDirectory(input.Path);
            // Shared relative payloads may only be shared if the bytes agree.
            var others = new CadCatalog { Modules = modules.Where(m => m.Id != module.Id).ToList() };
            var owned = CadFiles.PayloadFiles(others).Where(x => x != CadFiles.ManifestName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var relative in CadFiles.PayloadFiles(new() { Modules = [module] }).Where(x => x != CadFiles.ManifestName))
            {
                if (owned.Contains(relative) && CadFiles.Hash(CadFiles.Child(root, relative)) != CadFiles.Hash(CadFiles.Child(source, relative)))
                    throw new IOException("다른 CAD 기능과 파일 경로가 충돌합니다: " + relative);
                Copy(source, root, relative);
            }
            modules.RemoveAll(m => m.Id == module.Id); modules.Add(module);
        }
        // The retired preview command is removed only from the new prepared snapshot.
        modules = modules.Select(m => m.Id == "delhidden" ? m with { Actions = m.Actions.Where(a => a.Id != "preview").ToList() } : m).ToList();
        var catalog = new CadCatalog { Modules = modules };
        CadFiles.Save(Path.Combine(root, CadFiles.ManifestName), catalog);
        var known = modules.SelectMany(m => m.Actions.Select(a => CadFiles.Key(m,a))).ToHashSet();
        var selected = (bridge.Desired(status.Path)?.SelectedActions ?? []).Where(k => k != "delhidden:preview").ToHashSet();
        if (selected.Any(k => !known.Contains(k))) throw new IOException("기존 CAD 선택을 보존할 수 없습니다.");
        var key = package.ModuleId + ":" + package.CommandId;
        if (enabled) selected.Add(key); else selected.Remove(key);
        return bridge.Prepare(Catalog.Cad, staging, catalog, selected.ToArray(), session);
    }
    public CadRequest Rebase(CadButtonSource previous, IReadOnlyList<string> selection)
    {
        var status = new InstallService(paths).Status(Catalog.Cad);
        var staging = SafeFiles.Child(paths.Data, "Cad/FeatureStages/" + Guid.NewGuid().ToString("N") + "/" + Catalog.Cad.FolderName);
        SafeFiles.Copy(status.Path, staging, false);
        var root = CadReload.WindowsDirectory(staging);
        foreach (var file in CadFiles.PayloadFiles(previous.Catalog)) Copy(previous.Root, root, file);
        var catalog = previous.Catalog with { Modules = previous.Catalog.Modules.Select(m => m.Id == "delhidden" ? m with { Actions = m.Actions.Where(a => a.Id != "preview").ToList() } : m).ToList() };
        CadFiles.Save(Path.Combine(root, CadFiles.ManifestName), catalog);
        return new CadReload(paths).Prepare(Catalog.Cad, staging, catalog, selection.Where(k => k != "delhidden:preview").ToArray(), null);
    }
    private static void Copy(string source, string root, string relative)
    {
        var target = CadFiles.Child(root, relative); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(CadFiles.Child(source, relative), target, true);
    }
}
