using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace RoBIM.Manager.Core;

public sealed record GitHubIdentity(string Login, string Mode);
public sealed record GitHubAuth(string Mode = "Browser", string Login = "");

public static class GitHubSession
{
    private static string Settings => SafeFiles.Child(ManagerPaths.CurrentUser().Data, "github-auth.json");
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(3) };
    public static GitHubAuth Current()
    {
        var settings = File.Exists(Settings) ? JsonSerializer.Deserialize<GitHubAuth>(File.ReadAllText(Settings)) ?? new() : new();
        if (settings.Mode is not ("Browser" or "Token") ||
            (settings.Login.Length > 0 && !System.Text.RegularExpressions.Regex.IsMatch(settings.Login, "^[A-Za-z0-9-]{1,39}$")))
            throw new IOException("GitHub 연결 설정을 확인하십시오.");
        return settings;
    }
    private static void Save(GitHubAuth auth)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Settings)!);
        var temp = Settings + "." + Guid.NewGuid().ToString("N");
        File.WriteAllText(temp, JsonSerializer.Serialize(auth)); File.Move(temp, Settings, true);
    }
    internal static void ConfigureGit(System.Diagnostics.ProcessStartInfo info)
    {
        var auth = Current();
        if (auth.Mode == "Token")
        {
            var token = WindowsTokenStore.Get(auth.Login);
            info.Environment["GIT_CONFIG_COUNT"] = "2";
            info.Environment["GIT_CONFIG_KEY_1"] = "http.https://github.com/.extraheader";
            info.Environment["GIT_CONFIG_VALUE_1"] = "AUTHORIZATION: basic " + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(auth.Login + ":" + token));
        }
        else if (auth.Login.Length > 0)
        {
            info.Environment["GIT_CONFIG_COUNT"] = "2";
            info.Environment["GIT_CONFIG_KEY_1"] = "credential.https://github.com.username";
            info.Environment["GIT_CONFIG_VALUE_1"] = auth.Login;
        }
    }
    public static async Task<string> Credential()
    {
        var auth = Current();
        if (auth.Mode == "Token") return WindowsTokenStore.Get(auth.Login);
        var result = await ProcessRunner.Run("git", ["-c", "credential.helper=manager", "credential", "fill"],
            input: "protocol=https\nhost=github.com\n" + (auth.Login.Length > 0 ? "username=" + auth.Login + "\n" : "") + "\n");
        var token = result.Output.Split('\n').FirstOrDefault(x => x.StartsWith("password=", StringComparison.Ordinal))?[9..].TrimEnd('\r');
        if (result.ExitCode != 0 || string.IsNullOrEmpty(token)) throw new IOException("GitHub 계정 또는 토큰 연결이 필요합니다.");
        return token;
    }
    public static async Task<HttpResponseMessage> Request(string relative, string accept = "application/vnd.github+json", string? token = null)
    {
        // A relative API path only. Credentials cannot be forwarded to arbitrary hosts.
        if (!relative.StartsWith('/') || relative.StartsWith("//") || relative.Contains('\\') || relative.Contains("..")) throw new ArgumentException("GitHub API 경로 오류");
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com" + relative);
        request.Headers.UserAgent.ParseAdd("RoBIM-Manager/1.0"); request.Headers.Accept.ParseAdd(accept);
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        var credential = token ?? await Credential();
        if (credential.Length > 2000 || !System.Text.RegularExpressions.Regex.IsMatch(credential, "^[A-Za-z0-9_-]+$"))
            throw new IOException("토큰 형식이 올바르지 않습니다. GitHub에서 발급한 토큰을 확인하십시오.");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);
        var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
        { var code = (int)response.StatusCode; response.Dispose(); throw new IOException($"GitHub 접근 실패 ({code}). 계정·토큰 권한 또는 API 제한을 확인하십시오."); }
        return response;
    }
    private static async Task<string> Login(string token)
    {
        using var response = await Request("/user", token: token);
        if (!response.IsSuccessStatusCode) throw new IOException("GitHub 사용자 정보를 확인하지 못했습니다.");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("login").GetString() ?? throw new IOException("GitHub 계정명 누락");
    }
    public static async Task<GitHubIdentity> Identify()
    {
        var login = await Login(await Credential());
        return new(login, Current().Mode);
    }
    public static async Task<GitHubIdentity> UseBrowser(string login = "")
    {
        if (login.Length > 0 && !System.Text.RegularExpressions.Regex.IsMatch(login, "^[A-Za-z0-9-]{1,39}$")) throw new ArgumentException("GitHub 계정명 오류");
        // Verify the selected GCM account before replacing the active connection.
        var result = await ProcessRunner.Run("git", ["credential", "fill"], input: "protocol=https\nhost=github.com\n" + (login.Length > 0 ? "username=" + login + "\n" : "") + "\n");
        var token = result.Output.Split('\n').FirstOrDefault(x => x.StartsWith("password="))?[9..].TrimEnd('\r');
        if (result.ExitCode != 0 || token is null) throw new IOException("선택한 GitHub 계정의 인증을 확인하지 못했습니다.");
        var actual = await Login(token);
        if (login.Length > 0 && !login.Equals(actual, StringComparison.OrdinalIgnoreCase)) throw new IOException("선택한 계정과 인증된 GitHub 계정이 다릅니다.");
        Save(new("Browser", actual)); return new(actual, "Browser");
    }
    public static async Task<GitHubIdentity> UseToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 2000 || !System.Text.RegularExpressions.Regex.IsMatch(token, "^[A-Za-z0-9_-]+$"))
            throw new ArgumentException("GitHub에서 발급한 토큰을 공백 없이 입력하십시오.");
        var login = await Login(token);
        WindowsTokenStore.Save(login, token); Save(new("Token", login)); return new(login, "Token");
    }
    public static void RemoveToken()
    {
        var auth = Current(); if (auth.Mode != "Token") return;
        WindowsTokenStore.Remove(auth.Login); Save(new());
    }
}
