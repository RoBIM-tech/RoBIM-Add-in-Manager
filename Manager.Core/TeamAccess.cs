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
    private readonly bool usesSavedIdentity;
    private int generation;
    private readonly object stateSync = new();
    private readonly Func<Task<GitHubIdentity>> identify;
    private readonly Func<string, Task<HttpResponseMessage>> request;
    public TeamAccess(bool serve = true, Func<Task<GitHubIdentity>>? identify = null,
        Func<string, Task<HttpResponseMessage>>? request = null)
    {
        this.identify = identify ?? GitHubSession.Identify;
        usesSavedIdentity = identify is null;
        this.request = request ?? (route => GitHubSession.Request(route));
        if (serve) broker = new AccessBroker(() => !Allowed ? AccessPermissions.None : TestAllowed ? AccessPermissions.Stable | AccessPermissions.Test : AccessPermissions.Stable);
    }
    public bool Allowed => Volatile.Read(ref state) is { Result.Allowed: true } value &&
        Stopwatch.GetElapsedTime(value.Checked) < TimeSpan.FromMinutes(3);
    public TeamAccessResult Result => Volatile.Read(ref state).Result;
    public bool TestAllowed => Allowed && Volatile.Read(ref state).Result.TestAllowed;
    public bool CanUse(string channel) => channel switch { "stable" => Allowed, "test" => TestAllowed, _ => false };
    public void Require(string channel)
    {
        if (!CanUse(channel)) throw new IOException(channel == "test" ? "TEST 도구는 robim-test·robim-dev·robim-admin 팀만 다운로드·설치·실행할 수 있습니다." : channel == "stable" ? "GitHub 로그인과 RoBIM 팀 권한을 확인하십시오." : "배포 구분이 확인되지 않은 도구는 사용할 수 없습니다.");
    }
    public void Lock(string message = "GitHub 로그인이 필요합니다.")
    {
        lock (stateSync)
        {
            generation++;
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
                var activeTeams = new List<string>();
                var lookupFailed = false;
                foreach (var team in Teams)
                {
                    try
                    {
                        using var response = await request($"/orgs/{Organization}/teams/{team}/memberships/{user.Login}");
                        if (!response.IsSuccessStatusCode) { if (response.StatusCode != System.Net.HttpStatusCode.NotFound) lookupFailed = true; continue; }
                        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                        if (json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("state", out var membership) &&
                            membership.ValueKind == JsonValueKind.String && membership.GetString() == "active") activeTeams.Add(team);
                    }
                    catch (GitHubAuthenticationException) { throw; }
                    catch (Exception error) when (error is IOException or HttpRequestException or JsonException or OperationCanceledException) { lookupFailed = true; }
                }
                var role = activeTeams.Contains("robim-admin") ? "관리자" : activeTeams.Contains("robim-dev") ? "개발자" : activeTeams.Contains("robim-test") ? "TEST 사용자" : activeTeams.Contains(Team) ? "일반 사용자" : "";
                var test = activeTeams.Any(t => t != Team);
                result = new(activeTeams.Count > 0, user.Login, activeTeams.Count > 0 ? $"{role} · 정식" + (test ? " + TEST 사용 가능" : " 도구 사용 가능") : "RoBIM 팀의 활성 구성원만 사용할 수 있습니다. 팀 초대 수락·접근 권한을 확인하십시오.", test, role, activeTeams.Count > 0 ? AccountAccessState.Ready : lookupFailed ? AccountAccessState.RetryPending : AccountAccessState.TeamDenied);
                if (activeTeams.Count == 0 && lookupFailed) result = result with { Detail = "저장된 계정 유지 · 팀 권한 조회를 자동 재시도합니다. 연결이 복구될 때까지 도구는 잠깁니다." };
            }
            catch (GitHubAuthenticationException error)
            { result = new(false, usesSavedIdentity ? GitHubSession.Current().Login : "", error.Message, State: AccountAccessState.ReauthenticationRequired); }
            catch (Exception error) when (error is IOException or HttpRequestException or JsonException or System.ComponentModel.Win32Exception or OperationCanceledException)
            { result = new(false, usesSavedIdentity ? GitHubSession.Current().Login : "", "저장된 계정 유지 · GitHub 연결을 자동 재시도합니다. 연결이 복구될 때까지 도구는 잠깁니다.", State: AccountAccessState.RetryPending); }
            lock (stateSync)
                if (started == generation) Volatile.Write(ref state, new(result, Stopwatch.GetTimestamp()));
            return Result;
        }
        finally { gate.Release(); }
    }
    public void Dispose() { Lock(); broker?.Dispose(); }
}
