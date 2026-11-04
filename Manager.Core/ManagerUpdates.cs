using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RoBIM.Manager.Core;

public sealed record ManagerUpdate(string Version, long AssetId, string Digest);
public sealed record ManagerUpdateRequest(string Installer, string Digest, string Directory, int ParentId, long ParentStarted);

public static class ManagerUpdates
{
    private static readonly System.Net.Http.HttpClient PublicClient = new() { Timeout = TimeSpan.FromMinutes(3) };
    private static async Task<System.Net.Http.HttpResponseMessage> PublicRequest(string path, string accept = "application/vnd.github+json")
    {
        using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, "https://api.github.com" + path);
        request.Headers.UserAgent.ParseAdd("RoBIM-Manager/1.0");
        request.Headers.Accept.ParseAdd(accept);
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        var response = await PublicClient.SendAsync(request, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode) { var status = response.StatusCode; response.Dispose(); throw new IOException($"GitHub 업데이트 조회 실패 ({(int)status})"); }
        return response;
    }
    private const string Repository = "/repos/RoBIM-tech/RoBIM-Add-in-Manager";
    public static ManagerUpdate? Select(JsonElement releases, string current, bool allowPrerelease = false)
    {
        ManagerUpdate? selected = null;
        foreach (var release in releases.EnumerateArray())
        {
            if (release.GetProperty("draft").GetBoolean()) continue;
            var prerelease = release.GetProperty("prerelease").GetBoolean();
            if (prerelease && !allowPrerelease && !current.Contains('-')) continue;
            var version = (release.GetProperty("tag_name").GetString() ?? "").TrimStart('v');
            if (!Regex.IsMatch(version, @"^\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?$")) continue;
            if (!ReleaseUpdates.IsNewer(current, "", new(version, prerelease ? "test" : "stable"))) continue;
            var assets = release.GetProperty("assets").EnumerateArray().Where(a => a.GetProperty("name").GetString() == "RoBIM-Manager-Setup.exe").ToArray();
            if (assets.Length != 1) continue;
            var asset = assets[0]; var digest = asset.TryGetProperty("digest", out var d) ? d.GetString() ?? "" : "";
            if (!Regex.IsMatch(digest, "^sha256:[a-fA-F0-9]{64}$") || asset.GetProperty("id").GetInt64() <= 0) continue;
            if (selected is null || ReleaseUpdates.IsNewer(selected.Version, "", new(version, ""))) selected = new(version, asset.GetProperty("id").GetInt64(), digest[7..].ToLowerInvariant());
        }
        return selected;
    }
    public static async Task<ManagerUpdate?> Check(string current, bool allowPrerelease = false)
    {
        using var response = await PublicRequest(Repository + "/releases?per_page=100");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return Select(json.RootElement, current, allowPrerelease);
    }
    public static async Task<string> Download(ManagerPaths paths, ManagerUpdate update)
    {
        var directory = SafeFiles.Child(paths.Data, "ManagerUpdates/" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory); var file = SafeFiles.Child(directory, "RoBIM-Manager-Setup.exe");
        using var response = await PublicRequest(Repository + "/releases/assets/" + update.AssetId, "application/octet-stream");
        await using (var output = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            await response.Content.CopyToAsync(output);
        if (!string.Equals(SafeFiles.Hash(file), update.Digest, StringComparison.OrdinalIgnoreCase)) throw new IOException("Manager 업데이트 파일 검증에 실패했습니다. 기존 버전을 유지합니다.");
        return file;
    }
    public static bool NativeInstallation(string directory)
    {
        SafeFiles.NoLinks(directory);
        var marker = Path.Combine(directory, ".robim-manager-native.ini");
        return File.Exists(marker) && File.ReadAllLines(marker).Contains("Product=RoBIM.Manager") &&
            File.ReadAllLines(marker).Contains("Path=" + directory.TrimEnd(Path.DirectorySeparatorChar));
    }
    public static void Launch(ManagerPaths paths, string installer, string digest)
    {
        var directory = Path.GetDirectoryName(Environment.ProcessPath!)!;
        if (!NativeInstallation(directory)) throw new IOException("Manager 자동 업데이트는 Setup으로 설치한 버전에서 사용할 수 있습니다.");
        var staging = Path.GetDirectoryName(installer)!;
        var expected = SafeFiles.Child(paths.Data, "ManagerUpdates").TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(installer).StartsWith(expected, StringComparison.OrdinalIgnoreCase)) throw new IOException("업데이트 경로 오류");
        SafeFiles.NoLinks(installer);
        if (!string.Equals(SafeFiles.Hash(installer), digest, StringComparison.OrdinalIgnoreCase)) throw new IOException("Manager 업데이트 검증 실패");
        var helper = SafeFiles.Child(staging, "RoBIM.Manager.Update.exe");
        File.Copy(Environment.ProcessPath!, helper, false);
        var requestFile = SafeFiles.Child(staging, "request.json");
        using var parent = Process.GetCurrentProcess();
        File.WriteAllText(requestFile, JsonSerializer.Serialize(new ManagerUpdateRequest(installer,digest,directory,parent.Id,parent.StartTime.ToUniversalTime().Ticks)));
        var info = new ProcessStartInfo(helper) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        info.ArgumentList.Add("--apply-manager-update"); info.ArgumentList.Add(requestFile);
        _ = Process.Start(info) ?? throw new IOException("Manager 업데이트를 시작하지 못했습니다.");
    }
    public static int Apply(string requestFile)
    {
        ManagerUpdateRequest? request = null;
        try
        {
            SafeFiles.NoLinks(requestFile);
            request = JsonSerializer.Deserialize<ManagerUpdateRequest>(File.ReadAllText(requestFile)) ?? throw new IOException("업데이트 요청 오류");
            var staging = Path.GetDirectoryName(Environment.ProcessPath!)!;
            if (Path.GetFullPath(requestFile) != Path.Combine(staging,"request.json") || Path.GetFullPath(request.Installer) != Path.Combine(staging,"RoBIM-Manager-Setup.exe")) throw new IOException("업데이트 요청 경로 오류");
            if (!Regex.IsMatch(request.Digest,"^[a-f0-9]{64}$") || !NativeInstallation(request.Directory)) throw new IOException("업데이트 설치 기록 오류");
            try
            {
                using var parent = Process.GetProcessById(request.ParentId);
                if (parent.StartTime.ToUniversalTime().Ticks != request.ParentStarted) throw new IOException("Manager 업데이트 세션 불일치");
                if (!parent.WaitForExit(60000)) throw new IOException("Manager 종료를 기다리고 있습니다. 업데이트를 다시 시도하세요.");
            }
            catch (ArgumentException) { }
            SafeFiles.NoLinks(request.Installer);
            using var file = new FileStream(request.Installer, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (!string.Equals(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(file)),request.Digest,StringComparison.OrdinalIgnoreCase)) throw new IOException("Manager 업데이트 파일 검증 실패");
            var info = new ProcessStartInfo(request.Installer) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            foreach (var arg in new[] { "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/DIR=" + request.Directory, "/LOG=" + Path.Combine(staging,"setup.log") }) info.ArgumentList.Add(arg);
            using var setup = Process.Start(info) ?? throw new IOException("Manager 설치 시작 실패"); setup.WaitForExit();
            if (setup.ExitCode != 0) throw new IOException("Manager 업데이트 설치 실패: " + setup.ExitCode);
            Process.Start(new ProcessStartInfo(Path.Combine(request.Directory,"RoBIM.Manager.exe")) { UseShellExecute = true });
            File.WriteAllText(Path.Combine(staging,"result.txt"),"applied"); return 0;
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"result.txt"),error.Message);
            if (request is not null && Regex.IsMatch(request.Digest,"^[a-f0-9]{64}$") && NativeInstallation(request.Directory))
            {
                var updates=Directory.GetParent(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))!.FullName;
                File.WriteAllText(SafeFiles.Child(updates,"failed-digest.txt"),request.Digest);
                Process.Start(new ProcessStartInfo(Path.Combine(request.Directory,"RoBIM.Manager.exe")) { UseShellExecute=true });
            }
            return 1;
        }
    }
}
