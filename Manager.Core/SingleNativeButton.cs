using RoBIM.Revit.Reload;

namespace RoBIM.Manager.Core;

/// <summary>Resolve a registered one-button package without a separate discovery step.</summary>
public static class SingleNativeButton
{
    public static CatalogCommand? Registration(ExtensionRepository repository, ExtensionPackage package) =>
        BundledCatalog.Read().Commands.SingleOrDefault(c => c.Host == "revit" &&
            c.Repository.Equals(repository.Repository, StringComparison.OrdinalIgnoreCase) && c.PackageId == package.Id);

    public static NativeModule Validate(ExtensionRepository repository, ExtensionPackage package, SourcePackage source)
    {
        if (package.Host != "revit" || package.Distribution != "release") throw new IOException("독립 Revit 배포 패키지가 필요합니다.");
        var module = NativeProtocol.Module(source.Path);
        if (module.Commands.Length != 1) throw new IOException("이 저장소의 배포 패키지는 버튼 하나만 포함해야 합니다.");
        var expected = Registration(repository, package);
        if (expected is not null && (module.Id != expected.ModuleId || module.Commands[0].Id != expected.CommandId))
            throw new IOException("다운로드한 버튼의 ID가 Manager 등록 정보와 다릅니다. 설치하지 않습니다.");
        ModuleReleases.ValidateNative(source.Path);
        if (package.Channel != "unknown" && ModuleReleases.Native(source.Path, module.Id, module.Version).Channel != package.Channel)
            throw new IOException("다운로드한 버튼의 TEST/실사용 구분이 등록 정보와 다릅니다.");
        return module;
    }

    public static async Task<SourcePackage> Resolve(ManagerPaths paths, ExtensionRepository repository, ExtensionPackage package,
        Func<Task<SourcePackage>> download, bool refresh = false)
    {
        var manager = new NativeRevitCommands(paths);
        var expected = Registration(repository, package);
        // Older downloads used a directory called runtime. Folder names are not command identity.
        var cached = expected is null ? manager.CachedForFolder(package.Folder) : manager.Cached(expected.ModuleId);
        var source = !refresh && cached is not null ? cached : await download();
        Validate(repository, package, source);
        return manager.Cache(source);
    }
}

public static class ToolNames
{
    public static string Display(string name)
    {
        var value = name.Trim();
        if (value.StartsWith("RoBIM ", StringComparison.OrdinalIgnoreCase)) value = value[6..].TrimStart();
        return value.Length == 0 ? name : value;
    }
}
