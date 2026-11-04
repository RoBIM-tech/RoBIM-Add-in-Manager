using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace RoBIM.Manager.Core;

public sealed class Sources(ManagerPaths paths, Action<string>? requireChannel = null)
{
    private static readonly HttpClient Http = CreateClient();
    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("RoBIM-Manager/1.0");
        return client;
    }

    private static Task<string> Credential() => GitHubSession.Credential();

    private static async Task<HttpResponseMessage> Request(string url, string accept, bool stream = false)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await Credential());
        request.Headers.Accept.ParseAdd(accept);
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        var response = await Http.SendAsync(request, stream ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead);
        if (!response.IsSuccessStatusCode)
        {
            var code = (int)response.StatusCode;
            response.Dispose();
            throw new IOException($"GitHub 접근 실패 ({code}). 로그인과 비공개 저장소 접근 권한을 확인하십시오.");
        }
        return response;
    }

    public static async Task CheckConnection()
    {
        using var response = await Request("https://api.github.com/repos/RoBIM-tech/RoBIM-CAD-Loader", "application/vnd.github+json");
    }

    public async Task<SourcePackage> OfflineCad(string archive)
    {
        await Task.Yield();
        var digestFile = archive + ".sha256";
        if (!File.Exists(digestFile) || !SafeFiles.Hash(archive).Equals(File.ReadAllText(digestFile).Trim(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("동봉 CAD 패키지의 해시가 일치하지 않습니다.");
        return ExtractCad(archive, "동봉 " + SafeFiles.Hash(archive)[..12]);
    }

    private string NewCache()
    {
        var directory = SafeFiles.Child(paths.Data, "Downloads/" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private SourcePackage ExtractCad(string archive, string version)
    {
        var directory = NewCache();
        SafeFiles.Extract(archive, directory);
        var package = CadLoaderAssets.ExtractedBundle(directory);
        InstallService.ValidatePackage(Catalog.Cad, package);
        return new(package, version);
    }

    public async Task<SourcePackage> DownloadCad(string? channel = null)
    {
        var package = new ExtensionPackage(Catalog.Cad.Id, "RoBIM CAD", "autocad", Catalog.Cad.FolderName, Catalog.Cad.FolderName, "release", CadLoaderAssets.Name);
        if (channel is not null) package = package with { Channel = channel };
        var offer = await ReleaseUpdates.Check(new(Catalog.Cad.Repository, Catalog.Cad.Branch, [package], "legacy CAD"), package);
        requireChannel?.Invoke(offer.Channel);
        var assetId = offer.AssetId; var digest = offer.Digest; var version = offer.Version;
        var archive = Path.Combine(NewCache(), "package.zip");
        using (var download = await Request($"https://api.github.com/repos/RoBIM-tech/RoBIM-CAD-Loader/releases/assets/{assetId}", "application/octet-stream", true))
        using (var input = await download.Content.ReadAsStreamAsync())
        using (var output = File.Create(archive))
        {
            var buffer = new byte[81920];
            long total = 0;
            int count;
            while ((count = await input.ReadAsync(buffer)) != 0)
            {
                total += count;
                if (total > 128 * 1024 * 1024) throw new IOException("다운로드 크기 제한을 초과했습니다.");
                await output.WriteAsync(buffer.AsMemory(0, count));
            }
        }
        if (!SafeFiles.Hash(archive).Equals(digest[7..], StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("릴리스 파일의 SHA-256이 일치하지 않습니다.");
        return ExtractCad(archive, version!);
    }

    // Each host loader has an independent GitHub release lifecycle. Fetch only the
    // explicitly requested channel and verify GitHub's asset digest before use.
    public async Task<string> DownloadLoader(ToolSpec tool, string channel)
    {
        if (channel is not ("stable" or "test") || tool.Id != Catalog.Cad.Id && tool.Id != Catalog.RevitHost.Id)
            throw new IOException("지원하지 않는 로더 또는 배포 채널입니다.");
        requireChannel?.Invoke(channel);
        var asset = tool.Kind == ToolKind.AutoCAD ? CadLoaderAssets.Name : "RoBIM-Host-Revit2026.zip";
        using var response = await GitHubSession.Request("/repos/" + tool.Repository + "/releases?per_page=100");
        if (!response.IsSuccessStatusCode) throw new IOException($"{tool.Repository} 로더 저장소를 확인할 수 없습니다. 로그인한 계정의 저장소 읽기 권한과 조직 접근 승인을 확인하십시오. (HTTP {(int)response.StatusCode})");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var (assetId, digest) = SelectLoaderAsset(json.RootElement, asset, channel);
        var archive = Path.Combine(NewCache(), asset);
        using (var download = await Request("https://api.github.com/repos/" + tool.Repository + "/releases/assets/" + assetId, "application/octet-stream", true))
        using (var input = await download.Content.ReadAsStreamAsync())
        using (var output = File.Create(archive))
        {
            var buffer = new byte[81920]; long total = 0; int count;
            while ((count = await input.ReadAsync(buffer)) != 0)
            {
                total += count; if (total > 128L * 1024 * 1024) throw new IOException("로더 다운로드 크기 제한 초과");
                await output.WriteAsync(buffer.AsMemory(0, count));
            }
        }
        if (!SafeFiles.Hash(archive).Equals(digest[7..], StringComparison.OrdinalIgnoreCase)) throw new IOException("로더 SHA-256 검증 실패");
        File.WriteAllText(archive + ".sha256", digest[7..]);
        return archive;
    }

    public static string LoaderDllHash(string archive, string expected)
    {
        using var zip = System.IO.Compression.ZipFile.OpenRead(archive);
        var entries = zip.Entries.Where(e => e.FullName.Replace('\\', '/') == expected).ToArray();
        if (entries.Length != 1 || entries[0].Length is <= 0 or > 128L * 1024 * 1024)
            throw new IOException("저장소 로더 DLL이 없거나 중복됩니다.");
        using var input = entries[0].Open();
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(input));
    }

    public static ReleaseOffer? LoaderOffer(JsonElement releases, string asset, string channel)
    {
        ReleaseOffer? newest = null;
        foreach (var release in releases.EnumerateArray())
        {
            if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean() != (channel == "test")) continue;
            foreach (var candidate in release.GetProperty("assets").EnumerateArray().Where(a => CadLoaderAssets.Matches(asset, a.GetProperty("name").GetString())).OrderByDescending(a => a.GetProperty("name").GetString() == asset))
            {
                var digest = candidate.TryGetProperty("digest", out var d) ? d.GetString() : null;
                if (digest is null || !System.Text.RegularExpressions.Regex.IsMatch(digest, "^sha256:[a-fA-F0-9]{64}$") || candidate.GetProperty("size").GetInt64() is <= 0 or > 128L * 1024 * 1024)
                    throw new IOException("로더 릴리스 검증 정보 오류");
                var version = release.GetProperty("tag_name").GetString()!;
                if (version.StartsWith("host-v")) version = version[6..];
                else if (version.StartsWith('v')) version = version[1..];
                newest = NativeReleasePolicy.Newest(newest, new(version, channel, candidate.GetProperty("id").GetInt64(), digest));
            }
        }
        return newest;
    }
    public static (long Id, string Digest) SelectLoaderAsset(JsonElement releases, string asset, string channel)
    {
        if (channel is not ("stable" or "test")) throw new IOException("로더 채널 오류");
        var offer = LoaderOffer(releases, asset, channel) ?? throw new IOException("선택한 채널에 로더 릴리스가 없습니다.");
        return (offer.AssetId, offer.Digest);
    }
    public async Task<ReleaseChannels> CheckLoader(ToolSpec tool, bool includeTest)
    {
        var asset = tool.Id == Catalog.Cad.Id ? CadLoaderAssets.Name : tool.Id == Catalog.RevitHost.Id ? "RoBIM-Host-Revit2026.zip" : throw new IOException("로더 ID 오류");
        using var response = await GitHubSession.Request("/repos/" + tool.Repository + "/releases?per_page=100");
        return ParseLoaderResponse(tool.Repository, asset, includeTest, response.StatusCode, await response.Content.ReadAsStringAsync());
    }
    public static ReleaseChannels ParseLoaderResponse(string repository, string asset, bool includeTest, System.Net.HttpStatusCode status, string body)
    {
        if ((int)status is < 200 or >= 300)
            throw new IOException($"{repository} 로더 저장소를 확인할 수 없습니다. 로그인한 계정의 저장소 읽기 권한과 조직 접근 승인을 확인하십시오. (HTTP {(int)status})");
        using var json = JsonDocument.Parse(body);
        if (json.RootElement.ValueKind != JsonValueKind.Array)
            throw new IOException($"{repository} 로더 배포 목록의 응답 형식이 올바르지 않습니다. 잠시 후 다시 확인하십시오.");
        ReleaseOffer? stable = null, test = null; string? stableError = null, testError = null;
        try { stable = LoaderOffer(json.RootElement, asset, "stable"); } catch (IOException error) { stableError = error.Message; }
        if (includeTest) try { test = LoaderOffer(json.RootElement, asset, "test"); } catch (IOException error) { testError = error.Message; }
        return new(stable, test, stableError, testError);
    }

    public async Task<IReadOnlyList<string>> RevitBranches()
    {
        var result = await ProcessRunner.Run("git", ["ls-remote", "--heads", "https://github.com/" + Catalog.Revit.Repository + ".git"]);
        if (result.ExitCode != 0) throw new IOException("개발 브랜치를 조회하지 못했습니다. GitHub 로그인을 확인하십시오.");
        return result.Output.Split('\n').Where(x => x.Contains("refs/heads/"))
            .Select(x => x[(x.IndexOf("refs/heads/", StringComparison.Ordinal) + 11)..].Trim())
            .OrderBy(x => x == "main" ? 0 : 1).ThenBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task<SourcePackage> DownloadRevit(string? branch = null)
    {
        requireChannel?.Invoke("test");
        branch ??= Catalog.Revit.Branch;
        RevitPackages.ValidateBranch(branch);
        var directory = NewCache();
        var clone = Path.Combine(directory, "RoBIM.extension");
        var result = await ProcessRunner.Run("git", ["-c", "core.hooksPath=NUL", "-c", "core.longpaths=true", "clone", "--depth", "1", "--branch", branch,
            "https://github.com/" + Catalog.Revit.Repository + ".git", clone]);
        if (result.ExitCode != 0) throw new IOException("Revit 저장소 다운로드 실패. GitHub 로그인과 저장소 권한을 확인하십시오.");
        InstallService.ValidatePackage(Catalog.Revit, clone);
        var sha = await ProcessRunner.GitRequired(clone, "rev-parse", "HEAD");
        return new(clone, branch + "@" + sha[..12]);
    }
}
