using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace RoBIM.Manager.Core;

public sealed class Sources(ManagerPaths paths)
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
        using var response = await Request("https://api.github.com/repos/gagaggan/RoBIM-CAD", "application/vnd.github+json");
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
        var package = Path.Combine(directory, Catalog.Cad.FolderName);
        InstallService.ValidatePackage(Catalog.Cad, package);
        return new(package, version);
    }

    public async Task<SourcePackage> DownloadCad()
    {
        using var response = await Request("https://api.github.com/repos/gagaggan/RoBIM-CAD/releases?per_page=100", "application/vnd.github+json");
        using var data = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement? chosenAsset = null;
        string? version = null;
        foreach (var release in data.RootElement.EnumerateArray())
        {
            if (release.GetProperty("draft").GetBoolean()) continue;
            foreach (var asset in release.GetProperty("assets").EnumerateArray())
                if (asset.GetProperty("name").GetString() == "DelHiddenNet-AutoCAD2026.zip")
                { chosenAsset = asset; version = release.GetProperty("tag_name").GetString(); break; }
            if (chosenAsset is not null) break;
        }
        if (chosenAsset is null) throw new IOException("설치 가능한 CAD 릴리스가 없습니다.");
        var item = chosenAsset.Value;
        var assetId = item.GetProperty("id").GetInt64();
        if (item.GetProperty("size").GetInt64() > 128 * 1024 * 1024) throw new IOException("릴리스 파일이 크기 제한을 초과합니다.");
        var digest = item.TryGetProperty("digest", out var digestValue) ? digestValue.GetString() : null;
        if (digest is null || !digest.StartsWith("sha256:") || digest.Length != 71)
            throw new IOException("GitHub 릴리스의 SHA-256 검증 정보가 없습니다.");
        var archive = Path.Combine(NewCache(), "package.zip");
        using (var download = await Request($"https://api.github.com/repos/gagaggan/RoBIM-CAD/releases/assets/{assetId}", "application/octet-stream", true))
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
