using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using RoBIM.Access;

namespace RoBIM.Manager.Core;

public sealed record TeamAccessResult(bool Allowed, string Login, string Detail);

public sealed class TeamAccess : IDisposable
{
    public const string Organization = "RoBIM-tech", Team = "robim";
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
        if (serve) broker = new AccessBroker(() => Allowed);
    }
    public bool Allowed => Volatile.Read(ref state) is { Result.Allowed: true } value &&
        Stopwatch.GetElapsedTime(value.Checked) < TimeSpan.FromMinutes(3);
    public TeamAccessResult Result => Volatile.Read(ref state).Result;
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
                using var response = await request($"/orgs/{Organization}/teams/{Team}/memberships/{user.Login}");
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    result = new(false, user.Login, "RoBIM-tech / robim 팀의 활성 구성원만 사용할 수 있습니다. 팀 초대 수락·접근 권한을 확인하십시오.");
                else
                {
                    response.EnsureSuccessStatusCode();
                    using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                    var active = json.RootElement.TryGetProperty("state", out var membership) && membership.GetString() == "active";
                    result = new(active, user.Login, active ? "RoBIM-tech / robim 팀 인증 완료" : "팀 초대를 수락한 활성 구성원만 사용할 수 있습니다.");
                }
            }
            catch (Exception error) when (error is IOException or HttpRequestException or JsonException or System.ComponentModel.Win32Exception or OperationCanceledException)
            { result = new(false, "", "GitHub 팀 인증을 확인하지 못했습니다. 인터넷 연결·계정 권한(read:org)·조직 접근 승인을 확인하십시오."); }
            lock (stateSync)
                if (started == generation) Volatile.Write(ref state, new(result, Stopwatch.GetTimestamp()));
            return Result;
        }
        finally { gate.Release(); }
    }
    public void Dispose() { Lock(); broker?.Dispose(); }
}
