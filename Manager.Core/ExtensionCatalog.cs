using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RoBIM.Manager.Core;

public sealed record ExtensionPackage(string Id, string Name, string Host, string Path, string Folder,
    string Distribution = "git", string? Asset = null, string Description = "", string Channel = "unknown", string ModuleId = "", string CommandId = "");
public sealed record ExtensionManifest(int SchemaVersion, ExtensionPackage[] Packages);
public sealed record ExtensionRepository(string Repository, string Branch, ExtensionPackage[] Packages, string Recognition);
public sealed class NoInstallablePackagesException(string message) : IOException(message);
public sealed record CatalogResult(IReadOnlyList<ExtensionRepository> Repositories, string[] Warnings);

public sealed class ExtensionCatalog(ManagerPaths paths, Action<string>? requireChannel = null)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    private string FilePath => SafeFiles.Child(paths.Data, "extension-repositories.json");
    public static string Repository(string url)
    {
        var value = url.Trim();
        if (value.Contains("..")) throw new ArgumentException("저장소 URL 경로 오류");
        if (value.StartsWith("git@github.com:", StringComparison.OrdinalIgnoreCase)) value = "https://github.com/" + value[15..];
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "github.com" ||
            !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new ArgumentException("GitHub HTTPS Git URL을 입력하십시오. URL에 토큰이나 계정 비밀번호를 넣지 마십시오.");
        var match = Regex.Match(uri.AbsolutePath.TrimEnd('/'), @"^/([A-Za-z0-9-]+)/([A-Za-z0-9_.-]+)$");
        if (!match.Success) throw new ArgumentException("저장소 URL 형식: https://github.com/소유자/저장소.git");
        var name = match.Groups[2].Value;
        if (name.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        if (name.Length == 0 || name is "." or ".." || name.Contains("..")) throw new ArgumentException("저장소 이름 오류");
        return match.Groups[1].Value + "/" + name;
    }
    public static bool Related(IEnumerable<string> topics) => topics.Any(x => x is "robim-addon" or "autocad-plugin" or "revit-addin");
    public static ExtensionPackage[] ParseManifest(string text)
    {
        var manifest = JsonSerializer.Deserialize<ExtensionManifest>(text, Json) ?? throw new InvalidDataException("패키지 표시 정보가 비어 있습니다.");
        if (manifest.SchemaVersion != 1 || manifest.Packages is null || manifest.Packages.Length is < 1 or > 30)
            throw new InvalidDataException("robim-package.json의 schemaVersion / packages를 확인하십시오.");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in manifest.Packages)
        {
            if (package is null || string.IsNullOrWhiteSpace(package.Name) || package.Name.Length > 120 ||
                !Regex.IsMatch(package.Id ?? "", "^[a-z0-9][a-z0-9-]{0,63}$") || !ids.Add(package.Id!)) throw new InvalidDataException("패키지 id / name 오류 또는 중복");
            if (package.Channel is not ("unknown" or "test" or "stable")) throw new InvalidDataException("패키지 채널 오류");
            if (package.Host is not ("pyrevit" or "autocad" or "revit")) throw new InvalidDataException("지원 host: autocad, revit (기존 pyRevit 기록은 읽기 호환만 제공)");
            if (package.Host == "revit" && package.Distribution != "release") throw new InvalidDataException("독립 Revit 패키지는 검증된 Release ZIP이 필요합니다.");
            if (package.ModuleId is null || package.CommandId is null ||
                (package.ModuleId.Length > 0 || package.CommandId.Length > 0) &&
                (package.Host != "revit" || !Regex.IsMatch(package.ModuleId, "^[a-z][a-z0-9.-]{0,63}$") || !Regex.IsMatch(package.CommandId, "^[a-z][a-z0-9.-]{0,63}$")))
                throw new InvalidDataException("모듈·명령 ID를 함께 지정해야 합니다.");
            var suffix = package.Host == "pyrevit" ? ".extension" : ".bundle";
            if (package.Folder is null || !Regex.IsMatch(package.Folder, @"^[A-Za-z0-9][A-Za-z0-9 _.-]{0,100}$") ||
                !package.Folder.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) || package.Folder.Contains("..") || !folders.Add(package.Host + "/" + package.Folder))
                throw new InvalidDataException("패키지 folder 오류 또는 중복");
            if (string.IsNullOrWhiteSpace(package.Path)) throw new InvalidDataException("패키지 path 누락");
            if (package.Path != ".") _ = SafeFiles.Child(System.IO.Path.GetTempPath(), package.Path);
            if (package.Distribution is not ("git" or "release") || (package.Distribution == "release" &&
                (string.IsNullOrWhiteSpace(package.Asset) || System.IO.Path.GetFileName(package.Asset) != package.Asset || !package.Asset.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))))
                throw new InvalidDataException("배포 방식 또는 ZIP asset 오류");
        }
        return manifest.Packages;
    }
    private static ExtensionRepository? BuiltIn(string repo, string branch) => repo.ToLowerInvariant() switch
    {
        "robim-tech/robim-cad" => new(repo, branch, [new("delhidden", "DELHIDDEN .NET", "autocad", "DelHiddenNet.bundle", "DelHiddenNet.bundle", "release", "DelHiddenNet-AutoCAD2026.zip")], "RoBIM 기본 도구"),
        _ => null
    };
    public async Task<ExtensionRepository> Inspect(string url)
    {
        var repo = Repository(url);
        if (repo.EndsWith("/RoBIM-pyrevit", StringComparison.OrdinalIgnoreCase) ||
            repo.EndsWith("/RoBIM-pyrevit-extension", StringComparison.OrdinalIgnoreCase))
            throw new NoInstallablePackagesException("pyRevit은 사용하지 않습니다. 저장소 이력만 보존합니다.");
        using var response = await GitHubSession.Request("/repos/" + repo);
        if (response.StatusCode == HttpStatusCode.NotFound) throw new IOException("저장소를 찾을 수 없습니다. 주소와 계정·토큰의 저장소 접근 권한을 확인하십시오.");
        using var metadata = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var canonical = metadata.RootElement.GetProperty("full_name").GetString()!;
        var branch = metadata.RootElement.GetProperty("default_branch").GetString()!;
        RevitPackages.ValidateBranch(branch);
        using var manifest = await GitHubSession.Request("/repos/" + canonical + "/contents/robim-package.json?ref=" + Uri.EscapeDataString(branch));
        if (manifest.IsSuccessStatusCode)
        {
            using var document = JsonDocument.Parse(await manifest.Content.ReadAsStringAsync());
            if (document.RootElement.GetProperty("size").GetInt64() > 128 * 1024) throw new IOException("표시 정보 파일이 너무 큽니다.");
            var content = Encoding.UTF8.GetString(Convert.FromBase64String(document.RootElement.GetProperty("content").GetString()!));
            using var declaration = JsonDocument.Parse(content);
            if (declaration.RootElement.TryGetProperty("schemaVersion", out var schema) && schema.GetInt32() == 1 &&
                declaration.RootElement.TryGetProperty("packages", out var entries) && entries.ValueKind == JsonValueKind.Array && entries.GetArrayLength() == 0)
                throw new NoInstallablePackagesException("설치 도구가 없는 기반 저장소입니다.");
            var parsed = ParseManifest(content);
            if (parsed.All(x => x.Host == "pyrevit"))
                throw new NoInstallablePackagesException("pyRevit 패키지는 지원 대상에서 제외되었습니다.");
            return new(canonical, branch, parsed.Where(x => x.Host != "pyrevit").ToArray(), "robim-package.json");
        }
        if (BuiltIn(canonical, branch) is { } builtin) return builtin;
        // Preserve legacy repositories on GitHub but never infer .extension packages.
        throw new NoInstallablePackagesException("이 저장소에는 지원되는 독립 Revit/CAD 패키지가 없습니다.");

    }
    public ExtensionRepository[] Registered()
    {
        var bundled = BundledCatalog.Read();
        var defaults = bundled.Repositories;
        var repos = File.Exists(FilePath) ? JsonSerializer.Deserialize<ExtensionRepository[]>(File.ReadAllText(FilePath), Json) ?? [] : [];
        foreach (var repo in repos)
        {
            if (Repository("https://github.com/" + repo.Repository) != repo.Repository) throw new IOException("등록 저장소 경로 오류");
            RevitPackages.ValidateBranch(repo.Branch);
            ParseManifest(JsonSerializer.Serialize(new ExtensionManifest(1, repo.Packages)));
        }
        // Only superseded, centrally owned package entries are redirected. Reading
        // the registry never rewrites it, installs tools or drops unrelated entries.
        repos = CurrentSources(repos);
        return CurrentSources(defaults.Concat(RepositoryDiscovery.Cached(paths)).Concat(repos));
    }
    private static ExtensionRepository[] CurrentSources(IEnumerable<ExtensionRepository> repositories)
    {
        var moves = BundledCatalog.Read().RepositoryMoves ?? [];
        return repositories.Select(repo => repo with { Packages = repo.Packages.Where(package => package.Host != "pyrevit" && !moves.Any(move =>
            move.FromRepository.Equals(repo.Repository, StringComparison.OrdinalIgnoreCase) && move.PackageId.Equals(package.Id, StringComparison.OrdinalIgnoreCase) &&
            !(move.ToRepositories.Length == 1 && RepositoryOwnership.Canonical(move.FromRepository).Equals(move.ToRepositories[0], StringComparison.OrdinalIgnoreCase)))).ToArray() })
            .Where(repo => repo.Packages.Length > 0)
            .Select(repo => repo with { Repository = RepositoryOwnership.Canonical(repo.Repository) })
            .GroupBy(repo => repo.Repository, StringComparer.OrdinalIgnoreCase).Select(group => group.Last()).ToArray();
    }
    public void Register(ExtensionRepository repository)
    {
        _ = Repository("https://github.com/" + repository.Repository);
        ParseManifest(JsonSerializer.Serialize(new ExtensionManifest(1, repository.Packages)));
        Directory.CreateDirectory(paths.Data);
        var entries = Registered().Where(x => !x.Repository.Equals(repository.Repository, StringComparison.OrdinalIgnoreCase)).Append(repository).ToArray();
        var temp = FilePath + "." + Guid.NewGuid().ToString("N"); File.WriteAllText(temp, JsonSerializer.Serialize(entries, Json)); File.Move(temp, FilePath, true);
    }
    public async Task<CatalogResult> Discover()
    {
        var repos = new Dictionary<string, ExtensionRepository>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();
        for (var page = 1; ; page++)
        {
            using var response = await GitHubSession.Request($"/user/repos?per_page=100&page={page}&sort=updated&affiliation=owner,collaborator,organization_member");
            if (!response.IsSuccessStatusCode) throw new IOException("연결 계정의 저장소 목록을 확인하지 못했습니다.");
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            foreach (var item in json.RootElement.EnumerateArray())
            {
                if (item.GetProperty("disabled").GetBoolean()) continue;
                var name = item.GetProperty("full_name").GetString()!;
                var marked = item.TryGetProperty("topics", out var topics) && Related(topics.EnumerateArray().Select(x => x.GetString()!));
                if (!marked && BuiltIn(name, "main") is null) continue;
                try { repos[name] = await Inspect("https://github.com/" + name); }
                catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or InvalidDataException or ArgumentException)
                { warnings.Add(name + ": " + ex.Message); }
            }
            if (json.RootElement.GetArrayLength() < 100) break;
            if (page == 100) throw new IOException("저장소가 많아 자동 조회 한도를 초과했습니다. Git URL로 직접 등록하십시오.");
        }
        foreach (var entry in Registered())
        {
            if (repos.ContainsKey(entry.Repository)) continue;
            try { repos[entry.Repository] = await Inspect("https://github.com/" + entry.Repository); }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or ArgumentException)
            { repos[entry.Repository] = entry with { Recognition = "등록 사본 · 접근 확인 필요" }; warnings.Add(entry.Repository + ": " + ex.Message); }
        }
        return new(CurrentSources(repos.Values).OrderBy(x => x.Repository).ToArray(), warnings.ToArray());
    }
    public static ToolSpec Tool(ExtensionRepository repository, ExtensionPackage package)
    {
        var moves = BundledCatalog.Read().RepositoryMoves ?? [];
        var moved = moves.FirstOrDefault(move => move.FromRepository.Equals(repository.Repository, StringComparison.OrdinalIgnoreCase)
            && move.PackageId.Equals(package.Id, StringComparison.OrdinalIgnoreCase) && move.ToRepositories.Length == 1);
        repository = repository with { Repository = RepositoryOwnership.Canonical(moved?.ToRepositories[0] ?? repository.Repository) };
        if (repository.Repository.Equals(Catalog.Cad.Repository, StringComparison.OrdinalIgnoreCase) && package.Folder == Catalog.Cad.FolderName) return Catalog.Cad;
        if (repository.Repository.Equals(Catalog.Revit.Repository, StringComparison.OrdinalIgnoreCase) && package.Folder == Catalog.Revit.FolderName) return Catalog.Revit;
        if (new[] { Catalog.Revit.FolderName, Catalog.RevitDeveloper.FolderName, Catalog.RevitSelected.FolderName, Catalog.Cad.FolderName, Catalog.ReloadBridge.FolderName }.Contains(package.Folder, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("RoBIM 기본 도구의 예약 폴더명은 다른 저장소에서 사용할 수 없습니다.");
        // A one-to-one repository move keeps the legacy installation record key.
        // Module/command IDs continue to identify Native selections and ribbon state.
        var identity = moves.FirstOrDefault(move => move.ToRepositories.Length == 1 &&
            move.ToRepositories[0].Equals(repository.Repository, StringComparison.OrdinalIgnoreCase) && move.PackageId.Equals(package.Id, StringComparison.OrdinalIgnoreCase))?.FromRepository ?? RepositoryOwnership.LegacyIdentity(repository.Repository);
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity.ToLowerInvariant() + "/" + package.Id))).ToLowerInvariant()[..16];
        return new("ext-" + digest, ToolNames.Display(package.Name), package.Host == "autocad" ? ToolKind.AutoCAD : package.Host == "revit" ? ToolKind.NativeRevit : ToolKind.PyRevit,
            repository.Repository, repository.Branch, package.Folder, package.Description, true);
    }
    public static ExtensionPackage CurrentPackage(ExtensionPackage previous, ExtensionRepository current)
    {
        var package = current.Packages.SingleOrDefault(p => p.Id == previous.Id) ?? throw new IOException("기존 패키지 ID가 저장소에 없습니다.");
        if (package.Host != previous.Host || package.Path != previous.Path || package.Folder != previous.Folder ||
            package.Distribution != previous.Distribution || package.Asset != previous.Asset ||
            previous.ModuleId.Length > 0 && (package.ModuleId != previous.ModuleId || package.CommandId != previous.CommandId))
            throw new IOException("패키지 설치 경로나 배포 파일 정의가 변경되었습니다. 등록 정보를 검토하십시오.");
        return package;
    }
    public static ExtensionPackage ReleaseChannel(ExtensionPackage package, string channel)
    {
        if (package.Host != "revit" || package.Distribution != "release" || channel is not ("test" or "stable"))
            throw new IOException("독립 Revit 도구의 TEST 또는 정식 배포를 선택하십시오.");
        return package with { Channel = channel };
    }
    public async Task<SourcePackage> Download(ExtensionRepository repository, ExtensionPackage package, string? releaseChannel = null)
    {
        var refreshed = await Inspect("https://github.com/" + repository.Repository);
        if (!refreshed.Packages.Contains(package)) throw new IOException("저장소 표시 정보가 변경되었습니다. 목록을 새로 조회하십시오.");
        // An explicit user choice changes only release selection, never the registered default or package identity.
        if (releaseChannel is not null) package = ReleaseChannel(package, releaseChannel);
        var cache = SafeFiles.Child(paths.Data, "Extensions/" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cache);
        string root, version;
        if (package.Distribution == "git")
        {
            requireChannel?.Invoke(package.Channel);
            root = System.IO.Path.Combine(cache, "source");
            var result = await ProcessRunner.Run("git", ["-c", "core.hooksPath=NUL", "-c", "core.longpaths=true", "clone", "--depth", "1", "--branch", refreshed.Branch, "https://github.com/" + refreshed.Repository + ".git", root]);
            if (result.ExitCode != 0) throw new IOException("확장 소스 다운로드 실패. 연결 계정·토큰의 Contents 읽기 권한을 확인하십시오.");
            version = refreshed.Branch + "@" + (await ProcessRunner.GitRequired(root, "rev-parse", "HEAD"))[..12];
        }
        else
        {
            var offer = await ReleaseUpdates.Check(refreshed, package);
            requireChannel?.Invoke(offer.Channel);
            version = offer.Version;
            var digest = offer.Digest;
            using var download = await GitHubSession.Request("/repos/" + refreshed.Repository + "/releases/assets/" + offer.AssetId, "application/octet-stream");
            if (!download.IsSuccessStatusCode) throw new IOException("배포 다운로드 실패");
            var zip = System.IO.Path.Combine(cache, "package.zip");
            using (var input = await download.Content.ReadAsStreamAsync())
            using (var output = File.Create(zip))
            {
                var buffer = new byte[81920]; long total = 0; int count;
                while ((count = await input.ReadAsync(buffer)) != 0) { total += count; if (total > 128L * 1024 * 1024) throw new IOException("ZIP 다운로드 크기 초과"); await output.WriteAsync(buffer.AsMemory(0, count)); }
            }
            if (!SafeFiles.Hash(zip).Equals(digest[7..], StringComparison.OrdinalIgnoreCase)) throw new IOException("ZIP SHA-256 불일치");
            root = System.IO.Path.Combine(cache, "extracted"); SafeFiles.Extract(zip, root);
        }
        var source = package.Path == "." ? root : SafeFiles.Child(root, package.Path);
        var prepared = System.IO.Path.Combine(cache, "runtime", package.Folder); SafeFiles.Copy(source, prepared, false);
        InstallService.ValidatePackage(Tool(refreshed, package), prepared);
        if (package.Host == "revit")
        {
            var module = RoBIM.Revit.Reload.NativeProtocol.Module(prepared);
            requireChannel?.Invoke(ModuleReleases.Native(prepared, module.Id, module.Version).Channel);
        }
        return new(prepared, version);
    }
}
