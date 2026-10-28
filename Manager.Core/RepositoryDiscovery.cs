using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RoBIM.Manager.Core;

public sealed record RepositoryPublisher(string Name, string Kind = "auto", bool Enabled = true);
public sealed record RepositorySearchSettings(int SchemaVersion, bool IncludeConnectedAccount, RepositoryPublisher[] Publishers);
public sealed record RepositoryDiscoveryCache(int SchemaVersion, ExtensionRepository[] Repositories, Dictionary<string, string> Stamps);

/// <summary>Explicit metadata refresh only: never downloads packages or changes installed selections.</summary>
public sealed class RepositoryDiscovery(ManagerPaths paths,
    Func<string, Task<HttpResponseMessage>>? request = null,
    Func<string, Task<ExtensionRepository>>? inspect = null)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    private string SettingsPath => SafeFiles.Child(paths.Data, "repository-publishers.json");
    private string CachePath => SafeFiles.Child(paths.Data, "discovered-repositories.json");
    private Task<HttpResponseMessage> Request(string url) => request is null ? GitHubSession.Request(url) : request(url);
    private Task<ExtensionRepository> Inspect(string name) => inspect is null ? new ExtensionCatalog(paths).Inspect("https://github.com/" + name) : inspect(name);
    public static string PublisherName(string value)
    {
        value = value.Trim().TrimEnd('/');
        if (value.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase)) value = value[19..];
        if (!Regex.IsMatch(value, "^[A-Za-z0-9](?:[A-Za-z0-9-]{0,37}[A-Za-z0-9])?$"))
            throw new ArgumentException("제작자·조직 이름 또는 https://github.com/이름을 입력하세요.");
        return value;
    }
    public RepositorySearchSettings Settings()
    {
        var settings = File.Exists(SettingsPath)
            ? Validate(JsonSerializer.Deserialize<RepositorySearchSettings>(File.ReadAllText(SettingsPath), Json) ?? throw new IOException("검색 대상 설정이 비어 있습니다."))
            : new RepositorySearchSettings(1, true, [new("RoBIM-tech")]);
        if (settings.Publishers.Any(p => p.Name.Equals("RoBIM-tech", StringComparison.OrdinalIgnoreCase))) return settings;
        return settings with { Publishers = settings.Publishers.Select(p => p.Name.Equals("gagaggan", StringComparison.OrdinalIgnoreCase)
            ? p with { Name = "RoBIM-tech", Kind = "organization" } : p).ToArray() };
    }
    private static RepositorySearchSettings Validate(RepositorySearchSettings value)
    {
        if (value.SchemaVersion != 1 || value.Publishers is null || value.Publishers.Length > 50) throw new IOException("검색 대상 설정 형식 오류");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var owner in value.Publishers)
            if (owner is null || string.IsNullOrWhiteSpace(owner.Name) || PublisherName(owner.Name) != owner.Name || owner.Kind is not ("auto" or "user" or "organization") || !names.Add(owner.Name))
                throw new IOException("검색 대상 이름·종류 또는 중복을 확인하세요.");
        return value;
    }
    public void SaveSettings(RepositorySearchSettings value) => Save(SettingsPath, Validate(value));
    private static void Save<T>(string file, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var temporary = file + "." + Guid.NewGuid().ToString("N");
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, Json)); File.Move(temporary, file, true);
    }
    private RepositoryDiscoveryCache Cache()
    {
        if (!File.Exists(CachePath)) return new(1, [], new(StringComparer.OrdinalIgnoreCase));
        var value = JsonSerializer.Deserialize<RepositoryDiscoveryCache>(File.ReadAllText(CachePath), Json);
        if (value is not { SchemaVersion: 1, Repositories: not null, Stamps: not null } || value.Repositories.Length > 5000) throw new IOException("저장소 검색 캐시 형식 오류");
        foreach (var repo in value.Repositories)
        {
            if (repo is null) throw new IOException("검색 캐시 저장소 누락");
            _ = ExtensionCatalog.Repository("https://github.com/" + repo.Repository); RevitPackages.ValidateBranch(repo.Branch);
            ExtensionCatalog.ParseManifest(JsonSerializer.Serialize(new ExtensionManifest(1, repo.Packages)));
        }
        return value;
    }
    public static ExtensionRepository[] Cached(ManagerPaths paths)
    {
        try { return new RepositoryDiscovery(paths).Cache().Repositories; }
        catch (Exception ex) when (ex is IOException or JsonException or ArgumentException) { return []; }
    }
    public static bool Conflicts(ExtensionRepository entry, IEnumerable<ExtensionRepository> known)
    {
        foreach (var other in known.Where(r => !r.Repository.Equals(entry.Repository, StringComparison.OrdinalIgnoreCase)))
            foreach (var package in entry.Packages.Where(p => p.Host == "revit"))
                if (other.Packages.Any(p => p.Host == "revit" && (p.Id.Equals(package.Id, StringComparison.OrdinalIgnoreCase) ||
                    p.Folder.Equals(package.Folder, StringComparison.OrdinalIgnoreCase) ||
                    package.ModuleId.Length > 0 && p.ModuleId.Equals(package.ModuleId, StringComparison.OrdinalIgnoreCase)))) return true;
        return false;
    }
    public async Task<CatalogResult> Refresh()
    {
        var settings = Settings(); var warnings = new List<string>();
        RepositoryDiscoveryCache cache;
        try { cache = Cache(); }
        catch (Exception ex) when (ex is IOException or JsonException or ArgumentException) { warnings.Add("검색 캐시 확인 실패: " + ex.Message); cache = new(1, [], []); }
        var known = new ExtensionCatalog(paths).Registered().ToDictionary(r => r.Repository, StringComparer.OrdinalIgnoreCase);
        var candidates = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        async Task List(string route, string? owner = null)
        {
            for (int page = 1; page <= 100; page++)
            {
                using var response = await Request(route + (route.Contains('?') ? "&" : "?") + $"per_page=100&page={page}");
                if (!response.IsSuccessStatusCode) throw new IOException("목록 접근 실패 (" + (int)response.StatusCode + ")");
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                foreach (var item in document.RootElement.EnumerateArray())
                {
                    if (item.TryGetProperty("disabled", out var disabled) && disabled.GetBoolean() || item.TryGetProperty("archived", out var archived) && archived.GetBoolean()) continue;
                    var name = item.GetProperty("full_name").GetString()!;
                    if (settings.Publishers.Any(p => !p.Enabled && name.Split('/')[0].Equals(p.Name, StringComparison.OrdinalIgnoreCase))) continue;
                    if (owner is null && !settings.IncludeConnectedAccount && !settings.Publishers.Any(p => p.Enabled && name.Split('/')[0].Equals(p.Name, StringComparison.OrdinalIgnoreCase))) continue;
                    if (owner is not null && !name.Split('/')[0].Equals(owner, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!item.TryGetProperty("topics", out var topics) || !topics.EnumerateArray().Any(t => t.GetString() == "robim-addon")) continue;
                    candidates[name] = item.Clone();
                }
                if (document.RootElement.GetArrayLength() < 100) return;
            }
            throw new IOException("저장소 조회 한도 초과");
        }
        if (settings.IncludeConnectedAccount || settings.Publishers.Any(p => p.Enabled))
            try { await List("/user/repos?sort=updated&affiliation=owner,collaborator,organization_member"); }
            catch (Exception ex) { warnings.Add("연결 계정: " + ex.Message); }
        foreach (var owner in settings.Publishers.Where(o => o.Enabled))
        {
            try
            {
                using var response = await Request("/users/" + owner.Name);
                if (!response.IsSuccessStatusCode) throw new IOException("제작자·조직 접근 실패 (" + (int)response.StatusCode + ")");
                using var metadata = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var kind = metadata.RootElement.GetProperty("type").GetString() == "Organization" ? "organization" : "user";
                if (owner.Kind != "auto" && owner.Kind != kind) throw new IOException("설정한 개인/조직 종류가 GitHub 정보와 다릅니다.");
                await List((kind == "organization" ? "/orgs/" : "/users/") + owner.Name + "/repos?type=all", owner.Name);
            }
            catch (Exception ex) { warnings.Add(owner.Name + ": " + ex.Message); }
        }
        var stamps = new Dictionary<string, string>(cache.Stamps, StringComparer.OrdinalIgnoreCase);
        using var gate = new SemaphoreSlim(4);
        var inspected = await Task.WhenAll(candidates.Select(async pair =>
        {
            var stamp = pair.Value.GetProperty("default_branch").GetString() + ":" + (pair.Value.TryGetProperty("pushed_at", out var pushed) ? pushed.ToString() : "");
            if (known.TryGetValue(pair.Key, out var previous) && stamps.GetValueOrDefault(pair.Key) == stamp) return (pair.Key, Entry: previous, Stamp: stamp, Error: (string?)null);
            await gate.WaitAsync();
            try { return (pair.Key, Entry: await Inspect(pair.Key), Stamp: stamp, Error: (string?)null); }
            catch (NoInstallablePackagesException) { return (pair.Key, Entry: (ExtensionRepository?)null, Stamp: stamp, Error: (string?)null); }
            catch (Exception ex) { return (pair.Key, Entry: (ExtensionRepository?)null, Stamp: stamp, Error: ex.Message); }
            finally { gate.Release(); }
        }));
        foreach (var result in inspected)
        {
            try
            {
                if (result.Entry is null && result.Error is null) continue;
                if (result.Entry is null) throw new IOException(result.Error);
                if (!result.Entry.Repository.Equals(result.Key, StringComparison.OrdinalIgnoreCase)) throw new IOException("저장소 식별 정보가 다릅니다.");
                ExtensionCatalog.ParseManifest(JsonSerializer.Serialize(new ExtensionManifest(1, result.Entry.Packages)));
                if (known.TryGetValue(result.Key, out var previous)) foreach (var p in previous.Packages) ExtensionCatalog.CurrentPackage(p, result.Entry);
                if (result.Entry.Packages.Any(p => p.Host == "revit") && result.Entry.Packages.Length != 1) throw new IOException("독립 Revit 저장소는 패키지 하나만 제공해야 합니다.");
                if (Conflicts(result.Entry, known.Values)) throw new IOException("다른 저장소와 패키지·모듈 ID 또는 설치 폴더가 중복됩니다.");
                known[result.Key] = result.Entry; stamps[result.Key] = result.Stamp;
            }
            catch (Exception ex) { warnings.Add(result.Key + ": " + ex.Message); }
        }
        // Preserve known entries on access failure, owner disable/remove, or repository deletion.
        var repositories = known.Values.OrderBy(r => r.Repository).ToArray();
        Save(CachePath, new RepositoryDiscoveryCache(1, repositories, stamps));
        return new(repositories, warnings.ToArray());
    }
}
