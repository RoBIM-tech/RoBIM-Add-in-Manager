using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using RoBIM.Access;

namespace RoBIM.Manager.Core;

public enum AccountAccessState { SignedOut, Ready, RetryPending, ReauthenticationRequired, TeamDenied }
public sealed record TeamAccessResult(bool Allowed, string Login, string Detail, bool TestAllowed = false, string Role = "", AccountAccessState State = AccountAccessState.SignedOut);

public sealed class TeamAccess : IDisposable
{
    public const string Organization = "RoBIM-tech", Team = "robim";
    public static readonly string[] Teams = [Team, "robim-test", "robim-dev", "robim-admin"];
    private sealed record State(TeamAccessResult Result, long Checked);
    private State state = new(new(false, "", "GitHub 로그인이 필요합니다."), 0);
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly AccessBroker? broker;
    private readonly ToolAccessBroker? toolBroker;
    private readonly ToolRepositoryAccess? toolAccess;
    private readonly bool usesSavedIdentity;
    private int generation;
    private readonly object stateSync = new();
    private readonly Func<Task<GitHubIdentity>> identify;
    private readonly Func<string, Task<HttpResponseMessage>> request;
    public event Action? ToolsChanged;
    public TeamAccess(bool serve = true, Func<Task<GitHubIdentity>>? identify = null,
        Func<string, Task<HttpResponseMessage>>? request = null)
    {
        this.identify = identify ?? GitHubSession.Identify;
        usesSavedIdentity = identify is null;
        this.request = request ?? (route => GitHubSession.Request(route));
        if (serve) {
            toolAccess = new ToolRepositoryAccess(ManagerPaths.CurrentUser(), this.request);
            toolAccess.Changed += () => ToolsChanged?.Invoke();
            broker = new AccessBroker(() => !Allowed ? AccessPermissions.None : TestAllowed ? AccessPermissions.Stable | AccessPermissions.Test : AccessPermissions.Stable);
            toolBroker = new ToolAccessBroker(key => Allowed && toolAccess.Allows(key));
        }
    }
    public bool Allowed => Volatile.Read(ref state) is { Result.Allowed: true } value &&
        Stopwatch.GetElapsedTime(value.Checked) < TimeSpan.FromMinutes(3);
    public TeamAccessResult Result => Volatile.Read(ref state).Result;
    public bool TestAllowed => Allowed && Volatile.Read(ref state).Result.TestAllowed;
    public bool CanUseTool(string key) => Allowed && toolAccess?.Allows(key) == true;
    public ToolAccessStatus ToolStatus(string key) => toolAccess?.Status(key) ?? ToolAccessStatus.Checking;
    public bool CanUse(string channel) => channel switch { "stable" => Allowed, "test" => TestAllowed, _ => false };
    public void Require(string channel)
    {
        if (!CanUse(channel)) throw new IOException(channel == "test" ? "TEST 도구는 robim-test·robim-dev·robim-admin 팀만 다운로드·설치·실행할 수 있습니다." : channel == "stable" ? "GitHub 로그인과 RoBIM 팀 권한을 확인하십시오." : "배포 구분이 확인되지 않은 도구는 사용할 수 없습니다.");
    }
    public async Task RefreshTools()
    {
        await gate.WaitAsync();
        try {
            if (!Allowed || toolAccess is null) { toolAccess?.Clear(); return; }
            try { await toolAccess.Refresh(); }
            catch (Exception error) when (error is IOException or JsonException or HttpRequestException or OperationCanceledException) { toolAccess.Clear(); }
        } finally { gate.Release(); }
    }
    public void Lock(string message = "GitHub 로그인이 필요합니다.")
    {
        lock (stateSync)
        {
            generation++;
            toolAccess?.Clear();
            Volatile.Write(ref state, new(new(false, "", message), Stopwatch.GetTimestamp()));
        }
    }
    public async Task<TeamAccessResult> Refresh()
    {
        await gate.WaitAsync();
        try
        {
            // No browser prompt on startup: reuse the saved Windows credential only.
            if (usesSavedIdentity && GitHubSession.Current().Login.Length == 0)
            {
                Lock(); return Result;
            }
            TeamAccessResult result;
            int started; lock (stateSync) started = generation;
            try
            {
                var user = await identify();
                if (!System.Text.RegularExpressions.Regex.IsMatch(user.Login, "^[A-Za-z0-9-]{1,39}$")) throw new IOException("GitHub 계정명 오류");
                var activeTeams = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var lookupFailed = false;
                try
                {
                    for (var page = 1; ; page++)
                    {
                        if (page > 100) throw new IOException("팀 목록이 너무 깁니다.");
                        using var response = await request($"/user/teams?per_page=100&page={page}");
                        if (!response.IsSuccessStatusCode) throw new IOException("팀 목록 조회 실패");
                        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                        if (json.RootElement.ValueKind != JsonValueKind.Array) throw new IOException("팀 목록 형식 오류");
                        foreach (var entry in json.RootElement.EnumerateArray())
                        {
                            if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty("organization", out var org) || org.ValueKind != JsonValueKind.Object ||
                                !org.TryGetProperty("login", out var login) || login.ValueKind != JsonValueKind.String ||
                                !entry.TryGetProperty("slug", out var slug) || slug.ValueKind != JsonValueKind.String)
                                throw new IOException("팀 목록 형식 오류");
                            if (string.Equals(login.GetString(), Organization, StringComparison.OrdinalIgnoreCase))
                            {
                                var name = slug.GetString()!;
                                if (!System.Text.RegularExpressions.Regex.IsMatch(name, "^[A-Za-z0-9_-]{1,100}$")) throw new IOException("팀 식별자 오류");
                                using var membershipResponse = await request($"/orgs/{Organization}/teams/{name}/memberships/{user.Login}");
                                if (membershipResponse.StatusCode == System.Net.HttpStatusCode.NotFound) continue;
                                if (!membershipResponse.IsSuccessStatusCode) throw new IOException("팀 구성원 조회 실패");
                                using var membershipJson = JsonDocument.Parse(await membershipResponse.Content.ReadAsStringAsync());
                                if (!membershipJson.RootElement.TryGetProperty("state", out var membership) || membership.ValueKind != JsonValueKind.String) throw new IOException("팀 구성원 형식 오류");
                                if (membership.GetString() == "active") activeTeams.Add(name);
                            }
                        }
                        if (json.RootElement.GetArrayLength() < 100) break;
                    }
                }
                catch (GitHubAuthenticationException) { throw; }
                catch (Exception error) when (error is IOException or HttpRequestException or JsonException or OperationCanceledException) { lookupFailed = true; activeTeams.Clear(); }
                var role = activeTeams.Contains("robim-admin") ? "관리자" : activeTeams.Contains("robim-dev") ? "개발자" : activeTeams.Contains("robim-test") ? "TEST 사용자" : activeTeams.Count > 0 ? "일반 사용자" : "";
                var test = activeTeams.Any(t => t.Equals("robim-admin", StringComparison.OrdinalIgnoreCase) || t.Equals("robim-dev", StringComparison.OrdinalIgnoreCase) || t.Equals("robim-test", StringComparison.OrdinalIgnoreCase));
                result = new(activeTeams.Count > 0, user.Login, activeTeams.Count > 0 ? $"{role} · 정식" + (test ? " + TEST 사용 가능" : " 도구 사용 가능") : "RoBIM 팀의 활성 구성원만 사용할 수 있습니다. 팀 초대 수락·접근 권한을 확인하십시오.", test, role, activeTeams.Count > 0 ? AccountAccessState.Ready : lookupFailed ? AccountAccessState.RetryPending : AccountAccessState.TeamDenied);
                if (activeTeams.Count == 0 && lookupFailed) result = result with { Detail = "저장된 계정 유지 · 팀 권한 조회를 자동 재시도합니다. 연결이 복구될 때까지 도구는 잠깁니다." };
            }
            catch (GitHubAuthenticationException error)
            { result = new(false, usesSavedIdentity ? GitHubSession.Current().Login : "", error.Message, State: AccountAccessState.ReauthenticationRequired); }
            catch (Exception error) when (error is IOException or HttpRequestException or JsonException or System.ComponentModel.Win32Exception or OperationCanceledException)
            { result = new(false, usesSavedIdentity ? GitHubSession.Current().Login : "", "저장된 계정 유지 · GitHub 연결을 자동 재시도합니다. 연결이 복구될 때까지 도구는 잠깁니다.", State: AccountAccessState.RetryPending); }
            lock (stateSync)
                if (started == generation) Volatile.Write(ref state, new(result, Stopwatch.GetTimestamp()));
            if (!result.Allowed) toolAccess?.Clear();
            else if (started == generation && toolAccess is not null) _ = RefreshTools();
            return Result;
        }
        finally { gate.Release(); }
    }
    public void Dispose() { Lock(); toolBroker?.Dispose(); broker?.Dispose(); }
}
