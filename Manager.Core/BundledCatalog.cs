using System.Text.Json;
namespace RoBIM.Manager.Core;
public sealed record CatalogCommand(string Repository, string PackageId, string ModuleId, string CommandId, string Host);
public sealed record CatalogRepositoryMove(string FromRepository, string PackageId, string[] ToRepositories);
public sealed record ManagerCatalog(int SchemaVersion, ExtensionRepository[] Repositories, CatalogCommand[] Commands, CatalogRepositoryMove[]? RepositoryMoves = null);
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
        foreach (var repository in result.Repositories.Where(r => r.Packages.Any(p => p.Host == "revit")))
            if (repository.Packages.Length != 1 || result.Commands.Count(c => c.Repository.Equals(repository.Repository, StringComparison.OrdinalIgnoreCase)) != 1)
                throw new InvalidDataException("독립 Revit 저장소는 버튼/패키지 하나만 소유해야 합니다.");
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var command in result.Commands)
            if (!keys.Add(command.Host + ":" + command.ModuleId + ":" + command.CommandId) || !result.Repositories.Any(r => r.Repository == command.Repository && r.Packages.Any(p => p.Id == command.PackageId && p.Host == command.Host)))
                throw new InvalidDataException("중앙 버튼 등록 누락·중복");
        var moves = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var move in result.RepositoryMoves ?? [])
        {
            _ = ExtensionCatalog.Repository("https://github.com/" + move.FromRepository);
            if (string.IsNullOrWhiteSpace(move.PackageId) || !moves.Add(move.FromRepository + "/" + move.PackageId) || move.ToRepositories is null || move.ToRepositories.Length == 0 ||
                move.ToRepositories.Any(target => !result.Repositories.Any(r => r.Repository.Equals(target, StringComparison.OrdinalIgnoreCase))))
                throw new InvalidDataException("중앙 저장소 이전 정의 오류");
        }
        return result;
    }
}
