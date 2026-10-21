using System.Text.Json;
namespace RoBIM.Manager.Core;
public sealed record CatalogCommand(string Repository, string PackageId, string ModuleId, string CommandId, string Host);
public sealed record ManagerCatalog(int SchemaVersion, ExtensionRepository[] Repositories, CatalogCommand[] Commands);
public static class BundledCatalog
{
    public static ManagerCatalog Read()
    {
        using var stream = typeof(BundledCatalog).Assembly.GetManifestResourceStream("RoBIM.Manager.Catalog.json")!;
        var result = JsonSerializer.Deserialize<ManagerCatalog>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        if (result.SchemaVersion != 1 || result.Repositories is null || result.Commands is null) throw new InvalidDataException("중앙 목록 오류");
        foreach (var repository in result.Repositories)
        {
            _ = ExtensionCatalog.Repository("https://github.com/" + repository.Repository);
            _ = ExtensionCatalog.ParseManifest(JsonSerializer.Serialize(new ExtensionManifest(1, repository.Packages)));
        }
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var command in result.Commands)
            if (!keys.Add(command.Host + ":" + command.ModuleId + ":" + command.CommandId) || !result.Repositories.Any(r => r.Repository == command.Repository && r.Packages.Any(p => p.Id == command.PackageId && p.Host == command.Host)))
                throw new InvalidDataException("중앙 버튼 등록 누락·중복");
        return result;
    }
}
