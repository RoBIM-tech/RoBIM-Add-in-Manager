using System.Net.Http.Headers;
using System.Text.Json;

namespace RoBIM.Manager.Core;

public sealed record GitHubDeviceCode(string DeviceCode, string UserCode, Uri VerificationUri, int ExpiresIn, int Interval);

/// <summary>Device authorization over HTTPS, without Git or an embedded client secret.</summary>
public sealed class GitHubDeviceLogin
{
    public const string ClientId = "Ov23li7LpJktOPbfdQYY";
    private readonly HttpClient client;
    private readonly Func<TimeSpan, CancellationToken, Task> delay;
    private readonly TimeProvider time;
    public GitHubDeviceLogin(HttpClient? client = null, Func<TimeSpan, CancellationToken, Task>? delay = null, TimeProvider? time = null)
    {
        this.client = client ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) };
        this.delay = delay ?? Task.Delay;
        this.time = time ?? TimeProvider.System;
    }
    private async Task<JsonDocument> Post(string path, Dictionary<string, string> fields, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://github.com" + path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("RoBIM-Manager/1.0");
        request.Content = new FormUrlEncodedContent(fields);
        using var response = await client.SendAsync(request, cancellation);
        if (!response.IsSuccessStatusCode) throw new IOException($"GitHub 로그인 연결에 실패했습니다 ({(int)response.StatusCode}). 잠시 후 다시 시도하십시오.");
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
    }
    public async Task<GitHubDeviceCode> Begin(CancellationToken cancellation = default)
    {
        using var json = await Post("/login/device/code", new() { ["client_id"] = ClientId, ["scope"] = "repo read:org" }, cancellation);
        var root = json.RootElement;
        if (root.TryGetProperty("error", out _)) throw new IOException("GitHub 로그인 앱을 사용할 수 없습니다. 관리자에게 문의하십시오.");
        var uri = new Uri(root.GetProperty("verification_uri").GetString()!);
        if (uri.AbsoluteUri != "https://github.com/login/device") throw new IOException("GitHub 인증 주소를 확인할 수 없습니다.");
        var code = new GitHubDeviceCode(root.GetProperty("device_code").GetString()!, root.GetProperty("user_code").GetString()!, uri,
            root.GetProperty("expires_in").GetInt32(), root.TryGetProperty("interval", out var interval) ? interval.GetInt32() : 5);
        if (code.DeviceCode.Length is < 1 or > 200 || !System.Text.RegularExpressions.Regex.IsMatch(code.UserCode, "^[A-Z0-9]{4}-[A-Z0-9]{4}$") ||
            code.ExpiresIn is < 1 or > 1800 || code.Interval is < 1 or > 60) throw new IOException("GitHub 인증 응답이 올바르지 않습니다.");
        return code;
    }
    public async Task<string> Complete(GitHubDeviceCode code, CancellationToken cancellation = default)
    {
        var expires = time.GetUtcNow().AddSeconds(code.ExpiresIn);
        var interval = code.Interval;
        while (time.GetUtcNow() < expires)
        {
            await delay(TimeSpan.FromSeconds(interval), cancellation);
            if (time.GetUtcNow() >= expires) break;
            using var json = await Post("/login/oauth/access_token", new() { ["client_id"] = ClientId, ["device_code"] = code.DeviceCode,
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code" }, cancellation);
            var root = json.RootElement;
            if (root.TryGetProperty("access_token", out var token))
            {
                var value = token.GetString() ?? "";
                if (!System.Text.RegularExpressions.Regex.IsMatch(value, "^[A-Za-z0-9_-]{1,2000}$")) throw new IOException("GitHub 인증 응답이 올바르지 않습니다.");
                return value;
            }
            switch (root.TryGetProperty("error", out var problem) ? problem.GetString() : "")
            {
                case "authorization_pending": continue;
                case "slow_down": interval += 5; continue;
                case "access_denied": throw new IOException("GitHub 로그인이 취소되었습니다.");
                case "expired_token": throw new IOException("인증 코드가 만료되었습니다. 다시 로그인하십시오.");
                default: throw new IOException("GitHub 로그인을 완료하지 못했습니다. 다시 로그인하십시오.");
            }
        }
        throw new IOException("인증 코드가 만료되었습니다. 다시 로그인하십시오.");
    }
}
